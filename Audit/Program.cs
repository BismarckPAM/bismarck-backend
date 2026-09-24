using System.Text;
using Prometheus;
using Audit.Service.Data;
using Audit.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Database
var connectionString = builder.Configuration.GetConnectionString("AuditDatabase")
    ?? throw new InvalidOperationException("Connection string 'AuditDatabase' not found.");

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT Authentication & Authorization
var jwtKey = builder.Configuration["Jwt:Key"] 
    ?? builder.Configuration["Jwt:Secret"] 
    ?? throw new InvalidOperationException("JWT Secret Key is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero // Optional: removes the default 5-minute clock drift grace period
        };
    });

builder.Services.AddAuthorization();

// Application Services & Controllers
builder.Services.AddControllers();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddHostedService<KafkaAuditConsumer>();
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics();
app.Run();