using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Resource.Service.Data;
using Resource.Service.Filters;
using Resource.Service.Mappings;
using Resource.Service.Middleware;
using Resource.Service.Services;
using Resource.Service.Validators;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ModelStateValidationFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var resourceConnectionString = builder.Configuration.GetConnectionString("ResourceDatabase")
    + $";Password={builder.Configuration["RESOURCE_DB_PASSWORD"]}";

builder.Services.AddDbContext<ResourceDbContext>(options =>
    options.UseNpgsql(resourceConnectionString));
    
builder.Services.AddAutoMapper(config => config.AddProfile<MappingProfile>());
builder.Services.AddValidatorsFromAssemblyContaining<CreateResourceRequestValidator>();
builder.Services.AddScoped<IResourceService, ResourceService>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlerMiddleware>();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

public partial class Program { }
