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
    private readonly string _verificationEmailQueueName = configuration["ASB_VerificationEmailQueue"] ?? "verification-code-emails";

    [Function(nameof(GenerateVerificationCode))]
    public async Task Run(
        [ServiceBusTrigger("%ASB_VerificationRequestsQueue%", Connection = "ASB_ConnectionString")] ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        _logger.LogInformation("=== GenerateVerificationCode TRIGGERED ===");
        _logger.LogInformation("Listening on queue: verification-code-requests");
        _logger.LogInformation("Message ID: {MessageId}", message.MessageId);
        _logger.LogInformation("Message Body: {MessageBody}", message.Body.ToString());
        _logger.LogInformation("Will send to queue: {QueueName}", _verificationEmailQueueName);
        _logger.LogInformation("ASB_VerificationRequestsQueue config: {ConfigValue}", 
            System.Environment.GetEnvironmentVariable("ASB_VerificationRequestsQueue"));
        
        try
        {
            var messageBody = message.Body?.ToString();
            _logger.LogInformation("Processing verification request: {MessageBody}", messageBody);

            if (string.IsNullOrEmpty(messageBody))
            {
                _logger.LogWarning("Message body is null or empty");
                await messageActions.AbandonMessageAsync(message);
                return;
            }

            var evt = JsonConvert.DeserializeObject<VerificationCodeRequestedEvent>(messageBody);
            
            if (string.IsNullOrEmpty(evt?.Email))
            {
                _logger.LogWarning("Invalid request or missing email");
                await messageActions.AbandonMessageAsync(message);
                return;
            }

            // Generate verification code (stores in cache)
            var emailRequest = await _verificationService.GenerateVerificationCode(evt.Email);
            
            // Extract code from the email request
            var code = emailRequest.Code;
            _logger.LogInformation("Generated EmailRequest for {Email}: code={Code}", evt.Email, code);
            
            // Create event for EmailService with just email + code
            var verificationCodeSentEvent = new VerificationCodeSentEvent
            {
                Email = evt.Email,
                Code = code
            };
            
            _logger.LogInformation("Sending VerificationCodeSentEvent to verification-code-emails queue: Email={Email}, Code={Code}, EventJson={EventJson}",
                verificationCodeSentEvent.Email,
                verificationCodeSentEvent.Code,
                JsonConvert.SerializeObject(verificationCodeSentEvent));
            
            var sender = _serviceBusClient.CreateSender(_verificationEmailQueueName);
            await sender.SendMessageAsync(new ServiceBusMessage(JsonConvert.SerializeObject(verificationCodeSentEvent)));

            await messageActions.CompleteMessageAsync(message);
            _logger.LogInformation("Successfully sent verification code to verification-code-emails queue for EmailService: {Email}", evt.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating verification code");
            await messageActions.AbandonMessageAsync(message);
        }
    }
}