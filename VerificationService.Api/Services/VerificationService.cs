using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService.Api.Services;

public class VerificationService(HybridCache cache, ILogger<VerificationService> logger)
{
    private readonly HybridCache _cache = cache;
    private readonly ILogger<VerificationService> _logger = logger;
    private const int MaxAttempts = 5;
    private const int AttemptWindowMinutes = 10;

    private static int GenerateCode() => Random.Shared.Next(100000, 999999);

    public async Task<EmailRequest> GenerateVerificationCode(string email, CancellationToken cancellationToken = default)
    {
        var code = GenerateCode();
        await _cache.SetAsync(
            $"email-verification:{email}",
            code,
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            }, cancellationToken: cancellationToken);
        await _cache.RemoveAsync($"attempts:{email}", cancellationToken);
        return new EmailRequest
        {
            Recipients = [email],
            Code = code,
            Subject = "Verification Code",
            PlainText = $"Your verification code is: {code}",
            Html = $"<strong>Your verification code is: {code}</strong>"
        };
    }

    public async Task<bool> VerifyCodeAsync(string email, int code, CancellationToken cancellationToken = default)
    {
        var attemptsKey = $"attempts:{email}";
        var attempts = await _cache.GetOrCreateAsync(
            attemptsKey,
            cancel => ValueTask.FromResult(0),
            cancellationToken: cancellationToken
        );
        if (attempts >= MaxAttempts)
        {
            _logger.LogWarning("Too many failed verification attempts for {Email}.", email);
            return false;
        }

        var storedCode = await _cache.GetOrCreateAsync(
            $"email-verification:{email}",
            cancel => ValueTask.FromResult((int?)null),
            cancellationToken: cancellationToken
        );
        if (storedCode.HasValue && storedCode.Value == code)
        {
            await _cache.RemoveAsync($"email-verification:{email}", cancellationToken);
            await _cache.RemoveAsync(attemptsKey, cancellationToken);
            return true;
        }

        attempts++;
        await _cache.SetAsync(attemptsKey, attempts, new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(AttemptWindowMinutes),
            LocalCacheExpiration = TimeSpan.FromMinutes(AttemptWindowMinutes)
        }, cancellationToken: cancellationToken);
        _logger.LogWarning("Failed verification for {Email} (attempt {Attempt})", email, attempts);
        return false;
    }
}