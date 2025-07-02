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

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Configuration"];
});

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
});

builder.Build().Run();