using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VerificationService.Api.Contracts.Events;

namespace VerificationService.Api.Functions;

public class GenerateVerificationCode(ILogger<GenerateVerificationCode> logger, VerificationService.Api.Services.VerificationService verificationService)
{
    private readonly ILogger<GenerateVerificationCode> _logger = logger;
    private readonly VerificationService.Api.Services.VerificationService _verificationService = verificationService;

    [Function(nameof(GenerateVerificationCode))]
    [ServiceBusOutput("email-verification", Connection = "ASBConnection")]
    public async Task<string?> Run([ServiceBusTrigger("verification-requests", Connection = "ASBConnection")] ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions)
    {
        try
        {
            var messageBody = message.Body?.ToString();
            _logger.LogInformation("Processing verification request: {MessageBody}", messageBody);

            var evt = JsonConvert.DeserializeObject<VerificationCodeRequestedEvent>(messageBody);
            
            if (string.IsNullOrEmpty(evt?.Email))
            {
                _logger.LogWarning("Invalid request or missing email");
                await messageActions.AbandonMessageAsync(message);
                return null;
            }

            // Generate verification code (stores in cache)
            var emailRequest = _verificationService.GenerateVerificationCode(evt.Email);
            
            // Extract code from the email request
            var code = ExtractCodeFromEmailRequest(emailRequest);
            
            // Create event for EmailService with just email + code
            var verificationCodeSentEvent = new VerificationCodeSentEvent
            {
                Email = evt.Email,
                Code = code
            };

            await messageActions.CompleteMessageAsync(message);
            _logger.LogInformation("Verification code generated for: {Email}", evt.Email);
            
            return JsonConvert.SerializeObject(verificationCodeSentEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating verification code");
            await messageActions.AbandonMessageAsync(message);
            return null;
        }
    }

    private static int ExtractCodeFromEmailRequest(Contracts.Requests.EmailRequest emailRequest)
    {
        // Extract code from PlainText (format: "Your verification code is: 123456")
        var plainText = emailRequest.PlainText;
        var codeStart = plainText.LastIndexOf(": ", StringComparison.Ordinal) + 2;
        var codeString = plainText.Substring(codeStart).Trim();
        return int.Parse(codeString);
    }
}