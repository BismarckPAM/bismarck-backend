using Approval.Service.Data;
using Approval.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IApproverAuthorizationService, ApprovalService>();
builder.Services.AddSingleton<IDomainEventPublisher, KafkaDomainEventPublisher>();

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var signingKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
    ?? jwtSettings["SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
        };
    });
builder.Services.AddAuthorization();

var approvalConnectionString = builder.Configuration.GetConnectionString("ApprovalDatabase")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:ApprovalDatabase or ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<ApprovalDbContext>(options =>
    options.UseNpgsql(approvalConnectionString));
    

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

public partial class Program { }


