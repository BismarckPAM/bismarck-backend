using FluentValidation;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Mappings;
using Identity.Service.Middleware;
using Identity.Service.Services;
using Identity.Service.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using Confluent.Kafka;

var builder = WebApplication.CreateBuilder(args);

var allowedOrigin = builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste a JWT bearer token here."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});
var dbPassword = Environment.GetEnvironmentVariable("IDENTITY_DB_PASSWORD")
    ?? throw new InvalidOperationException("IDENTITY_DB_PASSWORD environment variable is required.");
var connectionString = builder.Configuration.GetConnectionString("IdentityDatabase") + $";Password={dbPassword}";

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddAutoMapper(config => config.AddProfile<MappingProfile>());
builder.Services.AddValidatorsFromAssemblyContaining<MappingProfile>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var signingKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? jwtSettings["SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
    throw new InvalidOperationException("JWT_SIGNING_KEY environment variable is required.");
builder.Configuration["JwtSettings:SigningKey"] = signingKey;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "kafka:9092";

app.MapPost("/api/test/kafka/publish", async (string message) =>
{
    var config = new ProducerConfig { BootstrapServers = kafkaBootstrap };
    using var producer = new ProducerBuilder<Null, string>(config).Build();
    var result = await producer.ProduceAsync("pam.test.events", new Message<Null, string>
    {
        Value = message
    });
    return Results.Ok(new
    {
        status = "Published",
        topic = result.Topic,
        partition = result.Partition.Value,
        offset = result.Offset.Value,
        payload = message
    });
});

app.MapGet("/api/test/kafka/consume", () =>
{
    var config = new ConsumerConfig
    {
        BootstrapServers = kafkaBootstrap,
        GroupId = "pam-test-consumer-group",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = true
    };
    using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
    consumer.Subscribe("pam.test.events");
    var consumeResult = consumer.Consume(TimeSpan.FromSeconds(15));
    if (consumeResult == null)
        return Results.Ok(new { message = "No messages found within timeout window." });

    return Results.Ok(new
    {
        status = "Consumed",
        topic = consumeResult.Topic,
        offset = consumeResult.Offset.Value,
        receivedMessage = consumeResult.Message.Value
    });
});

app.Run();

public partial class Program { }