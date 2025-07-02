using Azure.Communication.Email;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var acsConnectionString = builder.Configuration["ACS_ConnectionString"] 
    ?? throw new InvalidOperationException("ACS_ConnectionString is not set");

var asbConnectionString = builder.Configuration["ASB_ConnectionString"] 
    ?? throw new InvalidOperationException("ASB_ConnectionString is not set");

var asbVerificationRequestsQueue = builder.Configuration["ASB_VerificationRequestsQueue"] 
    ?? throw new InvalidOperationException("ASB_VerificationRequestsQueue is not set");

builder.Services.AddSingleton(_ => new EmailClient(acsConnectionString));
builder.Services.AddSingleton(_ => new ServiceBusClient(asbConnectionString));
builder.Services.AddSingleton<VerificationService.Api.Services.VerificationService>();

// Try to configure Redis, but don't fail if it doesn't work
try
{
    var redisConfig = Environment.GetEnvironmentVariable("Redis__Configuration") 
                      ?? builder.Configuration["Redis__Configuration"];
    
    Console.WriteLine($"[DEBUG] Redis config from env: '{Environment.GetEnvironmentVariable("Redis__Configuration")}'");
    Console.WriteLine($"[DEBUG] Redis config from builder: '{builder.Configuration["Redis__Configuration"]}'");
    Console.WriteLine($"[DEBUG] Final Redis config: '{redisConfig}'");
    
    if (!string.IsNullOrEmpty(redisConfig))
    {
        Console.WriteLine("[INFO] Attempting to configure Redis cache...");
        
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConfig;
        });

        builder.Services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });
        
        Console.WriteLine("[INFO] Redis cache configuration added successfully.");
    }
    else
    {
        Console.WriteLine("[WARNING] Redis configuration not found. Using in-memory cache only.");
        AddInMemoryCache(builder.Services);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[WARNING] Failed to configure Redis: {ex.Message}. Using in-memory cache only.");
    AddInMemoryCache(builder.Services);
}

builder.Build().Run();

static void AddInMemoryCache(IServiceCollection services)
{
    Console.WriteLine("[INFO] Configuring in-memory cache as fallback...");
    
    // Add in-memory cache as fallback
    services.AddMemoryCache();
    
    // Add HybridCache without Redis backend (will use only in-memory)
    services.AddHybridCache(options =>
    {
        options.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(5),
            LocalCacheExpiration = TimeSpan.FromMinutes(5)
        };
    });
    
    Console.WriteLine("[INFO] In-memory cache configured successfully.");
}