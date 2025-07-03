using System.Net;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using VerificationService.Api.Contracts.Events;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService.Api.Functions;


#if DEBUG
public class SendVerification(
    ILogger<SendVerification> logger, 
    ServiceBusClient busClient,
    IConfiguration configuration)
{
    private readonly ILogger<SendVerification> _logger = logger;
    private readonly ServiceBusClient _busClient = busClient;
    private readonly string _verificationQueueName = configuration["ASB_VerificationRequestsQueue"] ?? "verification-requests";

    [Function("SendVerification")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "verify")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("SendVerification HTTP endpoint called (DEV ONLY). Raw body: " + body);

            var request = JsonConvert.DeserializeObject<VerificationRequest>(body);

            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                _logger.LogWarning("Invalid email input.");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Email is required.");
                return badResponse;
            }
            
            var verificationRequestEvent = new VerificationCodeRequestedEvent
            {
                Email = request.Email
            };
            
            var messageJson = JsonConvert.SerializeObject(verificationRequestEvent);
            _logger.LogInformation("Sending verification request to queue: " + messageJson);
            
            var sender = _busClient.CreateSender(_verificationQueueName);
            await sender.SendMessageAsync(new ServiceBusMessage(messageJson));
            
            _logger.LogInformation("Verification request sent to queue for processing.");

            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteStringAsync($"Verification code request submitted for {request.Email}.");
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
}
#endif