using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VerificationService.Api.Contracts;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService.Api.Services;

public class VerificationService(IMemoryCache cache, ILogger<VerificationService> logger)
{
    private static readonly Random _randomizer = new();
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<VerificationService> _logger = logger;
    private const int MaxAttempts = 5;
    private const int AttemptWindowMinutes = 10;

    private static int GenerateCode() => _randomizer.Next(100000, 999999);

    public EmailRequest GenerateVerificationCode(string email)
    {
        var code = GenerateCode();
        _cache.Set(email, code, TimeSpan.FromMinutes(5));
        _cache.Remove($"attempts:{email}");
        return new EmailRequest
        {
            Recipients = [email],
            Subject = "Verification Code",
            PlainText = $"Your verification code is: {code}",
            Html = $@"
        <html>
          <body style='background-color:#f9f9fb; font-family:Arial, sans-serif; padding:20px; color:#20203A;'>
            <div style='max-width:500px; margin:0 auto; background-color:white; padding:30px; border-radius:8px; box-shadow:0 2px 8px rgba(0,0,0,0.05);'>
              <h2 style='color:#20203A; text-align:center;'>Verification Code</h2>
              <p style='font-size:16px; text-align:center;'>Your verification code is:</p>
              <div style='font-size:28px; font-weight:bold; text-align:center; margin:20px 0; color:#f36fff;'>
                {code}
              </div>
              <p style='text-align:center; font-size:14px; color:#777;'>
                Enter this code to verify your email address. This code is valid for a limited time.
              </p>
            </div>
          </body>
        </html>"
        };
    }

    public bool VerifyCode(string email, int code)
    {
        // Throttle: count attempts
        var attemptsKey = $"attempts:{email}";
        var attempts = _cache.Get<int?>(attemptsKey) ?? 0;
        if (attempts >= MaxAttempts)
        {
            _logger.LogWarning("Too many failed verification attempts for {Email}.", email);
            return false;
        }

        if (_cache.TryGetValue(email, out int storedCode) && storedCode == code)
        {
            _cache.Remove(email);
            _cache.Remove(attemptsKey);
            return true;
        }

        // Record failed attempt 
        attempts++;
        _cache.Set(attemptsKey, attempts, TimeSpan.FromMinutes(AttemptWindowMinutes));
        _logger.LogWarning("Failed verification for {Email} (attempt {Attempt})", email, attempts);
        return false;
    }
}