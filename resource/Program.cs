using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Resource.Service.Data;
using Resource.Service.Mappings;
using Resource.Service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var resourceConnectionString = builder.Configuration.GetConnectionString("ResourceDatabase")
    + $";Password={builder.Configuration["RESOURCE_DB_PASSWORD"]}";

builder.Services.AddDbContext<ResourceDbContext>(options =>
    options.UseNpgsql(resourceConnectionString));
    
builder.Services.AddAutoMapper(config => config.AddProfile<MappingProfile>());
builder.Services.AddValidatorsFromAssemblyContaining<MappingProfile>();
builder.Services.AddScoped<IResourceService, ResourceService>();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program { }
