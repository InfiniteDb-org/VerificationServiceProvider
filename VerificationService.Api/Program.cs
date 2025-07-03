using Azure.Communication.Email;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var acsConnectionString = builder.Configuration["ACS_ConnectionString"] 
    ?? throw new InvalidOperationException("ACS_ConnectionString is not set");

var asbConnectionString = builder.Configuration["ASB_ConnectionString"] 
    ?? throw new InvalidOperationException("ASB_ConnectionString is not set");

var redisConnectionString = builder.Configuration["Redis__Configuration"];

builder.Services.AddSingleton(_ => new EmailClient(acsConnectionString));
builder.Services.AddSingleton(_ => new ServiceBusClient(asbConnectionString));

// Add hybrid cache with optional Redis
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});

// Only add Redis if connection string is available
if (!string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
    });
}

builder.Services.AddSingleton<VerificationService.Api.Services.VerificationService>();

builder.Build().Run();