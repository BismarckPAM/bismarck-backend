using Approval.Service.Clients;
using Approval.Service.Data;
using Approval.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IApproverAuthorizationService, ApprovalService>();
builder.Services.AddSingleton<IDomainEventPublisher, KafkaDomainEventPublisher>();

// Best-effort enrichment clients (identity/resource display names). Failures
// degrade to null labels and never block request submission.
var identityServiceUrl = Environment.GetEnvironmentVariable("IDENTITY_SERVICE_URL")
    ?? builder.Configuration["IdentityService:BaseUrl"]
    ?? "http://localhost:5001";
var resourceServiceUrl = Environment.GetEnvironmentVariable("RESOURCE_SERVICE_URL")
    ?? builder.Configuration["ResourceService:BaseUrl"]
    ?? "http://localhost:5002";

builder.Services.AddHttpClient<IIdentityContextClient, IdentityContextClient>(client =>
    client.BaseAddress = new Uri(identityServiceUrl));
builder.Services.AddHttpClient<IResourceContextClient, ResourceContextClient>(client =>
    client.BaseAddress = new Uri(resourceServiceUrl));

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

// Behind the API Gateway TLS is already terminated, and internal traffic is
// plain HTTP. HttpsRedirection here would emit a 307 back to the client for
// every proxied request, so it must be switchable off.
if (!string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECTION"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

app.UseRouting();
app.UseHttpMetrics(); 
app.MapMetrics();

public partial class Program { }


