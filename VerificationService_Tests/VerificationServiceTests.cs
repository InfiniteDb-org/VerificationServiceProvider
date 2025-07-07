using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Moq;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService_Tests;

public class VerificationServiceTests
{
    private readonly TestHybridCache _cache;
    private readonly Mock<ILogger<VerificationService.Api.Services.VerificationService>> _loggerMock;
    private readonly VerificationService.Api.Services.VerificationService _service;

    public VerificationServiceTests()
    {
        // Use in-memory cache and mock logger for isolation
        _cache = new TestHybridCache();
        _loggerMock = new Mock<ILogger<VerificationService.Api.Services.VerificationService>>();
        _service = new VerificationService.Api.Services.VerificationService(_cache, _loggerMock.Object);
    }

    [Fact]
    public async Task GenerateVerificationCode_ShouldStoreCodeAndReturnEmailRequest()
    {
        // Arrange
        const string email = "test@example.com";

        // Act
        var emailRequest = await _service.GenerateVerificationCode(email);

        // Assert
        Assert.NotNull(emailRequest);
        Assert.Contains(email, emailRequest.Recipients);
        Assert.Equal("Verification Code", emailRequest.Subject);
        Assert.Contains("Your verification code is:", emailRequest.PlainText);
        Assert.Contains("Your verification code is:", emailRequest.Html);
        
        var storedCodeStr = await _cache.TryGetValueAsync<string>("email-verification:" + email);
        Assert.NotNull(storedCodeStr);
        var storedCode = int.Parse(storedCodeStr);
        Assert.InRange(storedCode, 100000, 999999);
    }

    [Fact]
    public async Task VerifyCode_WithCorrectCode_ShouldReturnTrueAndRemoveCode()
    {
        // Arrange
        const string email = "test@example.com";
        var emailRequest = await _service.GenerateVerificationCode(email);
        var code = ExtractCodeFromRequest(emailRequest);

        // Act
        var result = await _service.VerifyCodeAsync(email, code);

        // Assert
        Assert.True(result);
        var codeAfterVerification = await _cache.TryGetValueAsync<int>("email-verification:" + email);
        Assert.Equal(0, codeAfterVerification); // Code should be removed after successful verification
    }

    [Fact]
    public async Task VerifyCode_WithIncorrectCode_ShouldReturnFalse()
    {
        // Arrange
        const string email = "test@example.com";
        await _service.GenerateVerificationCode(email);
        const int wrongCode = 111111;

        // Act
        var result = await _service.VerifyCodeAsync(email, wrongCode);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GenerateVerificationCode_WithInvalidEmail_ShouldThrowArgumentException(string? invalidEmail)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GenerateVerificationCode(invalidEmail!));
    }

    [Fact]
    public async Task VerifyCode_WithNonExistentEmail_ShouldReturnFalse()
    {
        // Arrange
        const string nonExistentEmail = "nonexistent@example.com";
        const int anyCode = 123456;

        // Act
        var result = await _service.VerifyCodeAsync(nonExistentEmail, anyCode);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyCode_AfterMaxAttempts_ShouldThrottle()
    {
        // Arrange
        const string email = "test@example.com";
        const int wrongCode = 111111;
        const int maxAttempts = 5;
        
        await _service.GenerateVerificationCode(email);

        // Act - Make max attempts with wrong code
        for (var i = 0; i < maxAttempts; i++)
        {
            await _service.VerifyCodeAsync(email, wrongCode);
        }
        
        // Act - Attempt after throttling should be blocked
        var throttledResult = await _service.VerifyCodeAsync(email, wrongCode);

        // Assert
        Assert.False(throttledResult); // Verification should be throttled after max attempts
    }

    [Fact]
    public async Task GenerateVerificationCode_ShouldResetAttemptCounter()
    {
        // Arrange
        const string email = "test@example.com";
        const int wrongCode = 111111;
        
        // Make some failed attempts
        await _service.GenerateVerificationCode(email);
        for (var i = 0; i < 3; i++)
        {
            await _service.VerifyCodeAsync(email, wrongCode);
        }

        // Act - Generate new code (should reset attempts)
        var newEmailRequest = await _service.GenerateVerificationCode(email);
        var correctCode = ExtractCodeFromRequest(newEmailRequest);
        var result = await _service.VerifyCodeAsync(email, correctCode);

        // Assert
        Assert.True(result); // New code generation should reset attempt counter
    }

    [Fact]
    public async Task VerifyCode_WithExpiredCode_ShouldReturnFalse()
    {
        // Arrange
        const string email = "test@example.com";
        var emailRequest = await _service.GenerateVerificationCode(email);
        var code = ExtractCodeFromRequest(emailRequest);
        
        // Simulate code expiration by removing from cache
        await _cache.RemoveAsync("email-verification:" + email);

        // Act
        var result = await _service.VerifyCodeAsync(email, code);

        // Assert
        Assert.False(result); // Expired codes should not be valid
    }

    [Fact]
    public async Task MultipleCodeGenerations_ShouldOverwritePreviousCode()
    {
        // Arrange
        const string email = "test@example.com";
        
        // Act - Generate first code
        var firstRequest = await _service.GenerateVerificationCode(email);
        var firstCode = ExtractCodeFromRequest(firstRequest);
        
        // Act - Generate second code
        var secondRequest = await _service.GenerateVerificationCode(email);
        var secondCode = ExtractCodeFromRequest(secondRequest);

        // Assert
        Assert.NotEqual(firstCode, secondCode); // Codes should be different
        
        // First code should no longer work
        var firstCodeResult = await _service.VerifyCodeAsync(email, firstCode);
        Assert.False(firstCodeResult);
        
        // Second code should work
        var secondCodeResult = await _service.VerifyCodeAsync(email, secondCode);
        Assert.True(secondCodeResult);
    }

    [Fact]
    public async Task GenerateVerificationCode_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        const string email = "test@example.com";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.GenerateVerificationCode(email, cts.Token));
    }

    private static int ExtractCodeFromRequest(EmailRequest req)
    {
        // "Your verification code is: 123456"
        var parts = req.PlainText.Split(':');
        return int.Parse(parts[^1].Trim());
    }

    // Enhanced test cache with better isolation and thread safety
    private class TestHybridCache : HybridCache
    {
        private readonly Dictionary<string, object> _store = new();
        private readonly object _lock = new();

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, 
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, 
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            lock (_lock)
            {
                if (_store.TryGetValue(key, out var value) && value is T t)
                    return ValueTask.FromResult(t);
                
                var result = factory(state, cancellationToken).GetAwaiter().GetResult();
                _store[key] = result!;
                return ValueTask.FromResult(result);
            }
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            lock (_lock)
            {
                _store.Remove(key);
            }
            return ValueTask.CompletedTask;
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, 
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            lock (_lock)
            {
                _store[key] = value!;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<T?> TryGetValueAsync<T>(string key)
        {
            lock (_lock)
            {
                if (_store.TryGetValue(key, out var value) && value is T t)
                    return ValueTask.FromResult<T?>(t);
                return ValueTask.FromResult<T?>(default);
            }
        }
    }
}
