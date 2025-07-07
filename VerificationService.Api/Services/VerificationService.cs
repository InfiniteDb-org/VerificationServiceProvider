using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService.Api.Services;

public class VerificationService(HybridCache cache, ILogger<VerificationService> logger)
{
    private static readonly Random _randomizer = new();
    private readonly HybridCache _cache = cache;
    private readonly ILogger<VerificationService> _logger = logger;
    private const int MaxAttempts = 5;

    private static int GenerateCode() => _randomizer.Next(100000, 999999);

    // Generates and stores a verification code for the given email
    public async Task<EmailRequest> GenerateVerificationCode(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
            
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var code = GenerateCode();

        var codeOptions = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) };
        var cacheKey = $"email-verification:{normalizedEmail}";
        
        // Store code in cache and reset attempts
        await _cache.SetAsync(cacheKey, code.ToString(), codeOptions, tags: null, cancellationToken);
        await _cache.RemoveAsync($"email-verification:attempts:{normalizedEmail}", cancellationToken);

        /*_logger.LogInformation("Generated verification code for {Email}: {Code}", normalizedEmail, code);*/

        // Return email request with code
        return new EmailRequest
        {
            Recipients = [normalizedEmail],
            Code = code,
            Subject = "Verification Code",
            PlainText = $"Your verification code is: {code}",
            Html = $"<strong>Your verification code is: {code}</strong>"
        };
    }

    // Verifies the code for the given email, tracks failed attempts
    public async Task<bool> VerifyCodeAsync(string email, int code, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var codeKey = $"email-verification:{normalizedEmail}";
        var attemptsKey = $"email-verification:attempts:{normalizedEmail}";

        // check current failed attempts
        var attempts = await _cache.GetOrCreateAsync(attemptsKey, _ => ValueTask.FromResult(0), options: null, tags: null, cancellationToken);
        if (attempts >= MaxAttempts)
            return false;

        // check code match
        var storedCode = await _cache.GetOrCreateAsync(codeKey, _ => ValueTask.FromResult((string?)null), options: null, tags: null, cancellationToken);
        _logger.LogInformation("Code Key: {CodeKey}", codeKey);
        _logger.LogInformation("Stored Code: {StoredCode}", storedCode);
        
        if (!string.IsNullOrEmpty(storedCode) && storedCode == code.ToString())
        {
            // On success, clear code and attempts
            await _cache.RemoveAsync(codeKey, cancellationToken);
            await _cache.RemoveAsync(attemptsKey, cancellationToken);
            /*_logger.LogInformation("Successful verification for {Email}", normalizedEmail);*/
            return true;
        }

        // On failure, increment attempts
        var attemptsOptions = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) };
        await _cache.SetAsync(attemptsKey, attempts + 1, attemptsOptions, tags: null, cancellationToken);
        /*_logger.LogInformation("Attempts incremented to {Attempts}", attempts + 1);
        _logger.LogWarning("Failed verification for {Email} (attempt {Attempt})", normalizedEmail, attempts + 1);*/
        return false;
    }
}