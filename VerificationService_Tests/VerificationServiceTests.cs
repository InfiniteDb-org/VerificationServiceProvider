using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Moq;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService_Tests;

public class VerificationServiceTests
{
    // Minimal stub for HybridCache, only implements required members for test isolation
    private class TestHybridCache : HybridCache
    {
        private readonly Dictionary<string, object> _store = new();
        // Tag-based removal is a no-op in tests
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        // Emulates distributed cache for repeatable, isolated tests
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(key, out var value) && value is T t)
                return ValueTask.FromResult(t);
            var result = factory(state, cancellationToken).GetAwaiter().GetResult();
            _store[key] = result!;
            return ValueTask.FromResult(result);
        }
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            return ValueTask.CompletedTask;
        }
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            _store[key] = value!;
            return ValueTask.CompletedTask;
        }
        // Used for direct cache assertions in tests
        public ValueTask<T?> TryGetValueAsync<T>(string key)
        {
            if (_store.TryGetValue(key, out var value) && value is T t)
                return ValueTask.FromResult<T?>(t);
            return ValueTask.FromResult<T?>(default);
        }
    }

    [Fact]
    public async Task GenerateVerificationCode_ShouldStoreCodeAndReturnEmailRequest()
    {
        // Arrange
        var cache = new TestHybridCache();
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        var email = "test@example.com";

        // Act
        var emailRequest = await service.GenerateVerificationCode(email);

        // Assert
        Assert.NotNull(emailRequest);
        Assert.Contains(email, emailRequest.Recipients);
        Assert.Equal("Verification Code", emailRequest.Subject);
        Assert.Contains("Your verification code is:", emailRequest.PlainText);
        Assert.Contains("Your verification code is:", emailRequest.Html);
        // Ensure code is stored in cache
        var code = await cache.TryGetValueAsync<int>("email-verification:" + email);
        Assert.NotEqual(0, code);
        Assert.InRange(code, 100000, 999999);
    }

    [Fact]
    public async Task VerifyCode_ShouldReturnTrue_WhenCodeIsCorrect()
    {
        // Arrange
        var cache = new TestHybridCache();
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        const string email = "test@example.com";
        var emailRequest = await service.GenerateVerificationCode(email);
        var code = ExtractCodeFromRequest(emailRequest);

        // Act
        var result = await service.VerifyCodeAsync(email, code);

        // Assert
        Assert.True(result);
        // Code should be removed from cache after successful verification
        var codeAfterVerification = await cache.TryGetValueAsync<int>("email-verification:" + email);
        Assert.Equal(0, codeAfterVerification);
    }

    [Fact]
    public async Task VerifyCode_ShouldReturnFalse_WhenCodeIsIncorrect()
    {
        // Arrange
        var cache = new TestHybridCache();
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        const string email = "test@example.com";
        await service.GenerateVerificationCode(email);

        // Act
        var result = await service.VerifyCodeAsync(email, 111111); // Wrong code

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyCode_ShouldThrottleAfterMaxAttempts()
    {
        // Arrange
        var cache = new TestHybridCache();
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        var email = "test@example.com";
        await service.GenerateVerificationCode(email);

        // Act
        for (var i = 0; i < 5; i++)
            await service.VerifyCodeAsync(email, 111111); // Wrong code 5 times
        var result = await service.VerifyCodeAsync(email, 111111); // 6th attempt

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GenerateVerificationCode_ShouldResetAttempts()
    {
        // Arrange
        var cache = new TestHybridCache();
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        const string email = "test@example.com";
        await service.GenerateVerificationCode(email);
        for (var i = 0; i < 3; i++)
            await service.VerifyCodeAsync(email, 111111); // Wrong code
        // Simulate "too many attempts"
        for (var i = 0; i < 3; i++)
            await service.VerifyCodeAsync(email, 111111);
        // New code is generated, attempts should be reset
        var emailRequest = await service.GenerateVerificationCode(email);
        var code = ExtractCodeFromRequest(emailRequest);
        var result = await service.VerifyCodeAsync(email, code);

        // Assert
        Assert.True(result);
    }

    private static int ExtractCodeFromRequest(EmailRequest req)
    {
        // "Your verification code is: 123456"
        var part = req.PlainText.Split(':');
        return int.Parse(part[^1].Trim());
    }
}
