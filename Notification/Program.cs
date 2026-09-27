using Notification.Service.Data;
using Microsoft.EntityFrameworkCore;
using Notification.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Database
var connectionString = builder.Configuration.GetConnectionString("NotificationDatabase")
    ?? throw new InvalidOperationException("Connection string 'NotificationDatabase' not found.");

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));


// JWT Authentication & Authorization must match Identity's token contract.
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
    ?? jwtSettings["SigningKey"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException("JWT_SIGNING_KEY environment variable is required.");

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero // Optional: removes the default 5-minute clock drift grace period
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddControllers();
builder.Services.AddHostedService<KafkaNotificationConsumer>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();


app.Run();
app.UseRouting();
app.UseHttpMetrics(); 
app.MapMetrics();


