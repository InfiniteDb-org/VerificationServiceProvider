using System.Net;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using VerificationService.Api.Contracts.Events;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService.Api.Functions;

public class SendVerification(ILogger<SendVerification> logger, VerificationService.Api.Services.VerificationService verificationService, ServiceBusClient busClient)
{
    private readonly ILogger<SendVerification> _logger = logger;
    private readonly VerificationService.Api.Services.VerificationService _verificationService = verificationService;
    private readonly ServiceBusClient _busClient = busClient;

    [Function("SendVerification")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "verify")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("SendVerification HTTP endpoint called. Raw body: " + body);

            var request = JsonConvert.DeserializeObject<VerificationRequest>(body);
            _logger.LogInformation("Deserialized Email: " + request?.Email);

            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                _logger.LogWarning("Invalid email input.");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Email is required.");
                return badResponse;
            }

            // Generate verification code
            var emailRequest = await _verificationService.GenerateVerificationCode(request.Email);
            
            // Extract code and create event for EmailService
            var code = ExtractCodeFromEmailRequest(emailRequest);
            var verificationCodeSentEvent = new VerificationCodeSentEvent
            {
                Email = request.Email,
                Code = code
            };
            
            var messageJson = JsonConvert.SerializeObject(verificationCodeSentEvent);
            _logger.LogInformation("Message to be sent to EmailService: " + messageJson);

            // Send to EmailService queue
            var sender = _busClient.CreateSender("email-verification");
            await sender.SendMessageAsync(new ServiceBusMessage(messageJson));
            _logger.LogInformation("Message sent to EmailService queue.");

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteStringAsync($"Verification code sent to {request.Email}.");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in SendVerification HTTP endpoint.");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync("Internal error: " + ex.Message);
            return errorResponse;
        }
    }

    private static int ExtractCodeFromEmailRequest(EmailRequest emailRequest)
    {
        // Extract code from PlainText (format: "Your verification code is: 123456")
        var plainText = emailRequest.PlainText;
        var codeStart = plainText.LastIndexOf(": ", StringComparison.Ordinal) + 2;
        var codeString = plainText[codeStart..].Trim();
        return int.Parse(codeString);
    }
}