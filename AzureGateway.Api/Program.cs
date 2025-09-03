using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;
using AzureGateway.Api.Extensions;
using AzureGateway.Api.Hubs;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("./logs/gateway-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Azure Gateway API",
        Version = "v1",
        Description = "API for Azure Gateway - File monitoring and upload service"
    });
});

// Add CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5000")
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials();
    });
});

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add database services
builder.Services.AddDatabaseServices(builder.Configuration);

// Add file monitoring services
builder.Services.AddFileMonitoring();

builder.Services.AddUploadServices();

builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();

// Initialize database
await app.Services.InitializeDatabaseAsync();

// Seed configuration from appsettings.json
await app.Services.SeedConfigurationFromAppSettingsAsync(builder.Configuration);

try
{
    var azureValid = await app.Services.ValidateAzureConfigurationAsync();
    if (!azureValid)
    {
        Log.Warning("Azure Storage is not properly configured. Upload functionality will be limited.");
    }
}
catch (Exception ex)
{
    Log.Warning(ex, "Could not validate Azure configuration during startup");
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Azure Gateway API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowReactApp");

// Serve static files (React build)
app.UseStaticFiles();

app.MapHealthChecks("/health");

app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapHub<UploadStatusHub>("/uploadStatusHub");

// Fallback to serve React app
app.MapFallbackToFile("index.html");

// Log application startup
Log.Information("Azure Gateway application started");
Log.Information("Available endpoints:");
Log.Information("  - Swagger UI: /swagger");
Log.Information("  - Health Check: /health");
Log.Information("  - Upload Status Hub: /uploadStatusHub");

app.Run();
