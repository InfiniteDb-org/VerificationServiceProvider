namespace VerificationService.Api.Contracts.Requests;

public class EmailRequest
{
    public List<string> Recipients { get; set; } = null!;
    public string Subject { get; set; } = null!;
    public string PlainText { get; set; } = null!;
    public string Html { get; set; } = null!;
    public int Code { get; set; }
}