using FluentValidation;
using Identity.Service.Data;
using Identity.Service.Mappings;
using Identity.Service.Middleware;
using Identity.Service.Models;
using Identity.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    dbContext.Database.Migrate();

    var engineeringId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    var adminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var developerRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var viewerRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    if (!dbContext.Departments.Any(department => department.Id == engineeringId))
    {
        dbContext.Departments.Add(new Department { Id = engineeringId, Name = "Engineering" });
    }

    SeedRole(dbContext, adminRoleId, "Admin");
    SeedRole(dbContext, developerRoleId, "Developer");
    SeedRole(dbContext, viewerRoleId, "Viewer");

    SeedUser(dbContext, Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"), "QA Admin", "qa.admin@test.com", "QaTest@123", adminRoleId, engineeringId, true);
    SeedUser(dbContext, Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"), "QA Developer", "qa.developer@test.com", "QaTest@123", developerRoleId, engineeringId, true);
    SeedUser(dbContext, Guid.Parse("cccccccc-3333-3333-3333-333333333333"), "QA Deactivated", "qa.deactivated@test.com", "QaTest@123", viewerRoleId, engineeringId, false);

    dbContext.SaveChanges();
}

app.Run();

static void SeedRole(IdentityDbContext dbContext, Guid id, string name)
{
    if (!dbContext.Roles.Any(role => role.Id == id))
    {
        dbContext.Roles.Add(new Role { Id = id, Name = name });
    }
}

static void SeedUser(
    IdentityDbContext dbContext,
    Guid id,
    string fullName,
    string email,
    string password,
    Guid roleId,
    Guid departmentId,
    bool isActive)
{
    var user = dbContext.Users
        .IgnoreQueryFilters()
        .FirstOrDefault(user => user.Id == id || user.Email == email);

    if (user is null)
    {
        dbContext.Users.Add(new User
        {
            Id = id,
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = roleId,
            DepartmentId = departmentId,
            IsActive = isActive
        });
        return;
    }

    user.FullName = fullName;
    user.RoleId = roleId;
    user.DepartmentId = departmentId;
    user.IsActive = isActive;
}

public partial class Program { }
