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

    public async Task<EmailRequest> GenerateVerificationCode(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
            
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var code = GenerateCode();

        var codeOptions = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) };
        var cacheKey = $"email-verification:{normalizedEmail}";
        
        _logger.LogInformation("=== GENERATE CODE ===");
        _logger.LogInformation("Email: {Email}, Normalized: {NormalizedEmail}", email, normalizedEmail);
        _logger.LogInformation("Generated Code: {Code}", code);
        _logger.LogInformation("Cache Key: {CacheKey}", cacheKey);
        
        await _cache.SetAsync(cacheKey, code.ToString(), codeOptions, tags: null, cancellationToken);
        await _cache.RemoveAsync($"email-verification:attempts:{normalizedEmail}", cancellationToken);

        _logger.LogInformation("Code stored in cache successfully");
        
        // Verify the code was stored by trying to retrieve it
        var storedCode = await _cache.GetOrCreateAsync(cacheKey, _ => ValueTask.FromResult((string?)null), options: null, tags: null, cancellationToken);
        _logger.LogInformation("Verification - stored code retrieved: {StoredCode}", storedCode);

        _logger.LogInformation("Generated verification code for {Email}: {Code}", normalizedEmail, code);

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
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var codeKey = $"email-verification:{normalizedEmail}";
        var attemptsKey = $"email-verification:attempts:{normalizedEmail}";

        // Check attempts
        var attempts = await _cache.GetOrCreateAsync(attemptsKey, _ => ValueTask.FromResult(0), options: null, tags: null, cancellationToken);
        _logger.LogInformation("=== VERIFY CODE ===");
        _logger.LogInformation("Email: {Email}, Normalized: {NormalizedEmail}", email, normalizedEmail);
        _logger.LogInformation("Attempts Key: {AttemptsKey}", attemptsKey);
        _logger.LogInformation("Attempts: {Attempts}", attempts);
        
        if (attempts >= MaxAttempts)
        {
            _logger.LogWarning("Too many failed verification attempts for {Email}", normalizedEmail);
            return false;
        }

        // Check code
        var storedCode = await _cache.GetOrCreateAsync(codeKey, _ => ValueTask.FromResult((string?)null), options: null, tags: null, cancellationToken);
        _logger.LogInformation("Code Key: {CodeKey}", codeKey);
        _logger.LogInformation("Stored Code: {StoredCode}", storedCode);
        
        if (!string.IsNullOrEmpty(storedCode) && storedCode == code.ToString())
        {
            await _cache.RemoveAsync(codeKey, cancellationToken);
            await _cache.RemoveAsync(attemptsKey, cancellationToken);
            _logger.LogInformation("Successful verification for {Email}", normalizedEmail);
            return true;
        }

        // Increment attempts
        var attemptsOptions = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) };
        await _cache.SetAsync(attemptsKey, attempts + 1, attemptsOptions, tags: null, cancellationToken);
        _logger.LogInformation("Attempts incremented to {Attempts}", attempts + 1);
        _logger.LogWarning("Failed verification for {Email} (attempt {Attempt})", normalizedEmail, attempts + 1);
        
        return false;
    }
}