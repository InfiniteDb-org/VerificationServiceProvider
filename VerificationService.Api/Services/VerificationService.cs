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
            Code = code
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