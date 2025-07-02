namespace VerificationService.Api.Contracts.Events;

public class VerificationCodeSentEvent
{
    public string Email { get; set; } = null!;
    public int Code { get; set; }
}