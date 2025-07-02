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
        // Check cancellation early
        cancellationToken.ThrowIfCancellationRequested();
        
        // email validation
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var code = GenerateCode();
        var cacheKey = $"email-verification:{normalizedEmail}";
        _logger.LogInformation("HybridCache instance hash: {Hash}", _cache.GetHashCode());
        _logger.LogInformation("Generated verification code for {Email}: {Code}", normalizedEmail, code);
        _logger.LogInformation("Saving code {Code} to cache key {Key}", code, cacheKey);
        try
        {
            await _cache.SetAsync(
                cacheKey,
                code,
                new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(5),
                    LocalCacheExpiration = TimeSpan.FromMinutes(5)
                }, cancellationToken: cancellationToken);
            await _cache.RemoveAsync($"attempts:{normalizedEmail}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update cache for {Email}. Continuing with code generation.", normalizedEmail);
        }
        _logger.LogInformation("Returning EmailRequest for {Email}: code={Code}", normalizedEmail, code);
        return new EmailRequest
        {
            Recipients = [normalizedEmail],
            Code = code,
            Subject = "Verification Code",
            PlainText = $"Your verification code is: {code}",
            Html = $"<strong>Your verification code is: {code}</strong>"
        };
    }

    public async Task<bool> VerifyCodeAsync(string email, int code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Verification attempted with invalid email");
            return false;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var attemptsKey = $"attempts:{normalizedEmail}";
        var codeKey = $"email-verification:{normalizedEmail}";
        _logger.LogInformation("HybridCache instance hash: {Hash}", _cache.GetHashCode());
        _logger.LogInformation("Reading code from cache key {Key}", codeKey);
        try
        {
            var attempts = await _cache.GetOrCreateAsync(
                attemptsKey,
                _ => ValueTask.FromResult(0),
                cancellationToken: cancellationToken
            );

            if (attempts >= MaxAttempts)
            {
                _logger.LogWarning("Too many failed verification attempts for {Email}.", normalizedEmail);
                return false;
            }

            var storedCode = await _cache.GetOrCreateAsync(
                codeKey,
                _ => ValueTask.FromResult((int?)null),
                cancellationToken: cancellationToken
            );
            _logger.LogInformation("Stored code in cache for {Email}: {StoredCode}", normalizedEmail, storedCode);
            if (storedCode == code)
            {
                await _cache.RemoveAsync(codeKey, cancellationToken);
                await _cache.RemoveAsync(attemptsKey, cancellationToken);
                _logger.LogInformation("Successful verification for {Email}", normalizedEmail);
                return true;
            }

            attempts++;
            await _cache.SetAsync(attemptsKey, attempts, new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(AttemptWindowMinutes),
                LocalCacheExpiration = TimeSpan.FromMinutes(AttemptWindowMinutes)
            }, cancellationToken: cancellationToken);
            
            _logger.LogWarning("Failed verification for {Email} (attempt {Attempt})", normalizedEmail, attempts);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during verification for {Email}", normalizedEmail);
            return false;
        }
    }
}