
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
using Prometheus;
using System.Text;

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
builder.Services.AddScoped<IOnboardingTicketService, OnboardingTicketService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient<ITurnstileVerifier, CloudflareTurnstileVerifier>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IDomainEventPublisher, KafkaDomainEventPublisher>();

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

if (app.Environment.IsDevelopment()
    || app.Environment.IsStaging()
    || app.Environment.IsEnvironment("Testing")
    || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint(
        "/swagger/v1/swagger.json",
        "Identity Service v1");

    if (app.Configuration.GetValue<bool>("Swagger:Enabled"))
    {
        options.SwaggerEndpoint(
            "/swagger-docs/resource/v1.json",
            "Resource Service v1");

        options.SwaggerEndpoint(
            "/swagger-docs/authorization/v1.json",
            "Authorization Service v1");

        options.SwaggerEndpoint(
            "/swagger-docs/approval/v1.json",
            "Approval Service v1");

        options.SwaggerEndpoint(
            "/swagger-docs/audit/v1.json",
            "Audit Service v1");

        options.SwaggerEndpoint(
            "/swagger-docs/notification/v1.json",
            "Notification Service v1");

        options.SwaggerEndpoint(
            "/swagger-docs/analytics/v1.json",
            "Analytics Service v1");
    }
});

}


if (!string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECTION"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseCors("AllowFrontend");

// Prometheus instrumentation
app.UseHttpMetrics();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMetrics("/metrics");

await app.RunAsync();

public partial class Program { }
