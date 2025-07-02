namespace VerificationService.Api.Contracts.Requests;

public class VerifyVerificationRequest
{
    public string? Email { get; set; }
    public int Code { get; set; }
}