namespace VerificationService.Api.Contracts.Events;

public class VerificationCodeRequestedEvent
{
    public string UserId { get; set; } = null!;
    public string Email { get; set; } = null!;
}