var builder = WebApplication.CreateBuilder(args);

var dbPassword = Environment.GetEnvironmentVariable("APPROVAL_DB_PASSWORD")
    ?? throw new InvalidOperationException("APPROVAL_DB_PASSWORD environment variable is required.");

var approvalConnectionString = builder.Configuration.GetConnectionString("ApprovalDatabase")
    + $";Password={dbPassword}";

builder.Services.AddDbContext<ApprovalDbContext>(options =>
    options.UseNpgsql(approvalConnectionString));
    

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();



app.Run();


