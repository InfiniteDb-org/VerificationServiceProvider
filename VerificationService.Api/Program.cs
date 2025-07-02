using Azure.Communication.Email;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
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

builder.Services.AddSingleton(_ => new EmailClient(acsConnectionString));
builder.Services.AddSingleton(_ => new ServiceBusClient(asbConnectionString));
builder.Services.AddSingleton<VerificationService.Api.Services.VerificationService>();

// Skapa logger (efter att DI är satt upp)
using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var logger = loggerFactory.CreateLogger("CacheConfigurator");

VerificationService.Api.CacheConfigurator.Configure(builder.Services, builder.Configuration, logger);

builder.Build().Run();