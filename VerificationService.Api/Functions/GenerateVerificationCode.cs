using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using VerificationService.Api.Contracts.Events;

namespace VerificationService.Api.Functions;

public class GenerateVerificationCode(ILogger<GenerateVerificationCode> logger, VerificationService.Api.Services.VerificationService verificationService, ServiceBusClient serviceBusClient, IConfiguration configuration)
{
    private readonly ILogger<GenerateVerificationCode> _logger = logger;
    private readonly VerificationService.Api.Services.VerificationService _verificationService = verificationService;
    private readonly ServiceBusClient _serviceBusClient = serviceBusClient;
    private readonly string _queueName = configuration["ASB_VerificationRequestsQueue"] ?? throw new InvalidOperationException("ASB_VerificationRequestsQueue is not configured.");

    [Function(nameof(GenerateVerificationCode))]
    public async Task Run(
        [ServiceBusTrigger("%ASB_VerificationRequestsQueue%", Connection = "ASB_ConnectionString")] ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        try
        {
            var messageBody = message.Body?.ToString();
            _logger.LogInformation("Processing verification request: {MessageBody}", messageBody);

            if (messageBody != null)
            {
                var evt = JsonConvert.DeserializeObject<VerificationCodeRequestedEvent>(messageBody);
            
                if (string.IsNullOrEmpty(evt?.Email))
                {
                    _logger.LogWarning("Invalid request or missing email");
                    await messageActions.AbandonMessageAsync(message);
                    return;
                }

                // Generate verification code (stores in cache)
                var emailRequest = _verificationService.GenerateVerificationCode(evt.Email);
            
                // Extract code from the email request
                var code = emailRequest.Code;
            
                // Create event for EmailService with just email + code
                var verificationCodeSentEvent = new VerificationCodeSentEvent
                {
                    Email = evt.Email,
                    Code = code
                };

                // Send to queue specified by environment variable
                var sender = _serviceBusClient.CreateSender(_queueName);
                await sender.SendMessageAsync(new ServiceBusMessage(JsonConvert.SerializeObject(verificationCodeSentEvent)));

                await messageActions.CompleteMessageAsync(message);
                _logger.LogInformation("Verification code generated and sent for: {Email}", evt.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating verification code");
            await messageActions.AbandonMessageAsync(message);
        }
    }
}