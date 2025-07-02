using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<VerificationService.Api.Services.VerificationService>();


builder.Services.AddSingleton<ServiceBusClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["ASBConnection"];

    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("ASBConnection is not configured.");

    return new ServiceBusClient(connectionString);
});
builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();