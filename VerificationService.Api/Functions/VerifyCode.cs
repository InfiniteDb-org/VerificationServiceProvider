using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VerificationService.Api.Contracts;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService.Api.Functions;

public class VerifyCode(ILogger<VerifyCode> logger, VerificationService.Api.Services.VerificationService verificationService)
{
    private readonly ILogger<VerifyCode> _logger = logger;
    private readonly VerificationService.Api.Services.VerificationService _verificationService = verificationService;

    [Function("VerifyCode")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "verify-code")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            /*_logger.LogInformation("Request body: " + body);*/

            var request = JsonConvert.DeserializeObject<VerifyVerificationRequest>(body);
            if (request is null || string.IsNullOrWhiteSpace(request.Email))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("Invalid request payload.");
                return bad;
            }

            // Check if code is valid for given email
            var isValid = await _verificationService.VerifyCodeAsync(request.Email, request.Code);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(isValid ? "Code is valid." : "Code is invalid.");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying code.");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync("Internal error.");
            return error;
        }
    }
}