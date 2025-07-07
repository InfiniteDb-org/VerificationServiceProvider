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
        try
        {
            var messageBody = message.Body?.ToString();
            /*_logger.LogInformation("Processing verification request: {MessageBody}", messageBody);*/

            if (string.IsNullOrEmpty(messageBody))
            {
                await messageActions.AbandonMessageAsync(message);
                return;
            }

            var evt = JsonConvert.DeserializeObject<VerificationCodeRequestedEvent>(messageBody);
            
            if (string.IsNullOrEmpty(evt?.Email))
            {
                /*_logger.LogWarning("Invalid request or missing email");*/
                await messageActions.AbandonMessageAsync(message);
                return;
            }

            // Generate and store verification code for the email
            var emailRequest = await _verificationService.GenerateVerificationCode(evt.Email);
            var code = emailRequest.Code;
            /*_logger.LogInformation("Generated EmailRequest for {Email}: code={Code}", evt.Email, code);*/
            
            // Send event to email queue with email and code
            var verificationCodeSentEvent = new VerificationCodeSentEvent {Email = evt.Email, Code = code };
            var sender = _serviceBusClient.CreateSender(_verificationEmailQueueName);
            await sender.SendMessageAsync(new ServiceBusMessage(JsonConvert.SerializeObject(verificationCodeSentEvent)));

            await messageActions.CompleteMessageAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating verification code");
            await messageActions.AbandonMessageAsync(message);
        }
    }
}