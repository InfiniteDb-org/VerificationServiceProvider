using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using VerificationService.Api.Contracts.Requests;

namespace VerificationService_Tests;

public class VerificationServiceTests
{
    [Fact]
    public void GenerateVerificationCode_ShouldStoreCodeAndReturnEmailRequest()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        var email = "test@example.com";

        // Act
        var emailRequest = service.GenerateVerificationCode(email);

        // Assert
        Assert.NotNull(emailRequest);
        Assert.Contains(email, emailRequest.Recipients);
        Assert.Equal("Verification Code", emailRequest.Subject);
        Assert.Contains("Your verification code is:", emailRequest.PlainText);
        Assert.Contains("Your verification code is:", emailRequest.Html);
        // Ensure code is stored in cache
        Assert.True(cache.TryGetValue(email, out int code));
        Assert.True(code is >= 100000 and <= 999999);
    }

    [Fact]
    public void VerifyCode_ShouldReturnTrue_WhenCodeIsCorrect()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        var email = "test@example.com";
        var emailRequest = service.GenerateVerificationCode(email);
        var code = ExtractCodeFromRequest(emailRequest);

        // Act
        var result = service.VerifyCode(email, code);

        // Assert
        Assert.True(result);
        // Code should be removed from cache after successful verification
        Assert.False(cache.TryGetValue(email, out _));
    }

    [Fact]
    public void VerifyCode_ShouldReturnFalse_WhenCodeIsIncorrect()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        var email = "test@example.com";
        service.GenerateVerificationCode(email);

        // Act
        var result = service.VerifyCode(email, 111111); // Wrong code

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyCode_ShouldThrottleAfterMaxAttempts()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        var email = "test@example.com";
        service.GenerateVerificationCode(email);

        // Act
        for (int i = 0; i < 5; i++)
            service.VerifyCode(email, 111111); // Wrong code 5 times
        var result = service.VerifyCode(email, 111111); // 6th attempt

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GenerateVerificationCode_ShouldResetAttempts()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<VerificationService.Api.Services.VerificationService>>();
        var service = new VerificationService.Api.Services.VerificationService(cache, logger);
        var email = "test@example.com";
        service.GenerateVerificationCode(email);
        for (int i = 0; i < 3; i++)
            service.VerifyCode(email, 111111); // Wrong code
        // Simulate "too many attempts"
        for (int i = 0; i < 3; i++)
            service.VerifyCode(email, 111111);
        // New code is generated, attempts should be reset
        service.GenerateVerificationCode(email);
        var emailRequest = service.GenerateVerificationCode(email);
        var code = ExtractCodeFromRequest(emailRequest);
        var result = service.VerifyCode(email, code);

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
