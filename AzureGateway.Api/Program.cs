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

Log.Information("Starting Azure Gateway API initialization...");

// Add services to the container
Log.Information("Adding core services...");
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
Log.Information("Core services added successfully");

// Add CORS for React frontend
Log.Information("Configuring CORS policy...");
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
Log.Information("CORS policy configured for React frontend");

// Add SignalR for real-time updates
Log.Information("Adding SignalR services...");
builder.Services.AddSignalR();
Log.Information("SignalR services added successfully");

// Add database services
Log.Information("Adding database services...");
builder.Services.AddDatabaseServices(builder.Configuration);
Log.Information("Database services added successfully");

// Add file monitoring services
Log.Information("Adding file monitoring services...");
builder.Services.AddFileMonitoring();
Log.Information("File monitoring services added successfully");

Log.Information("Adding upload services...");
builder.Services.AddUploadServices();
Log.Information("Upload services added successfully");

Log.Information("Adding health checks...");
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();
Log.Information("Health checks added successfully");

var app = builder.Build();
Log.Information("Web application built successfully");

// Initialize database
Log.Information("Starting database initialization...");
try
{
    await app.Services.InitializeDatabaseAsync();
    Log.Information("Database initialized successfully");
}
catch (Exception ex)
{
    Log.Error(ex, "Failed to initialize database");
    throw;
}

// Seed configuration from appsettings.json
Log.Information("Starting configuration seeding...");
try
{
    await app.Services.SeedConfigurationFromAppSettingsAsync(builder.Configuration);
    Log.Information("Configuration seeded successfully");
}
catch (Exception ex)
{
    Log.Error(ex, "Failed to seed configuration");
    throw;
}

// Validate Azure configuration
Log.Information("Validating Azure Storage configuration...");
try
{
    var azureValid = await AzureGateway.Api.Extensions.UploadServiceExtensions.ValidateAzureConfigurationAsync(app.Services);
    if (!azureValid)
    {
        Log.Warning("Azure Storage is not properly configured. Upload functionality will be limited.");
    }
    else
    {
        Log.Information("Azure Storage configuration validated successfully");
    }
}
catch (Exception ex)
{
    Log.Warning(ex, "Could not validate Azure configuration during startup");
}

// Configure the HTTP request pipeline
Log.Information("Configuring HTTP request pipeline...");
if (app.Environment.IsDevelopment())
{
    Log.Information("Development environment detected - enabling Swagger");
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Azure Gateway API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowReactApp");
Log.Information("CORS middleware configured");

// Serve static files (React build)
app.UseStaticFiles();
Log.Information("Static file serving configured");

app.MapHealthChecks("/health");
Log.Information("Health check endpoint mapped");

app.UseRouting();
app.UseAuthorization();
Log.Information("Routing and authorization configured");

app.MapControllers();
app.MapHub<UploadStatusHub>("/uploadStatusHub");
Log.Information("Controllers and SignalR hub mapped");

// Fallback to serve React app
app.MapFallbackToFile("index.html");
Log.Information("Fallback routing configured for React app");

// Log application startup
Log.Information("=== Azure Gateway API Startup Complete ===");
Log.Information("Application environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("Available endpoints:");
Log.Information("  - Swagger UI: /swagger");
Log.Information("  - Health Check: /health");
Log.Information("  - Upload Status Hub: /uploadStatusHub");
Log.Information("  - API Controllers: /api/*");
Log.Information("Database connection: {ConnectionString}", 
    builder.Configuration.GetConnectionString("DefaultConnection")?.Replace("Data Source=", "").Replace("\\", "/"));
Log.Information("Log file location: ./logs/gateway-.log");
Log.Information("=== Starting application... ===");

app.Run();
