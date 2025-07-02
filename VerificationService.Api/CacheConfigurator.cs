using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VerificationService.Api;

public static class CacheConfigurator
{
    public static void Configure(IServiceCollection services, IConfiguration configuration, ILogger logger)
    {
        var redisConfig = Environment.GetEnvironmentVariable("Redis__Configuration")
                          ?? configuration["Redis__Configuration"];
        var hasRedisConfig = !string.IsNullOrEmpty(redisConfig);
        logger.LogInformation($"[DEBUG] Redis config available: {hasRedisConfig}");

        // Always add MemoryCache as fallback
        services.AddMemoryCache();
        logger.LogInformation("[INFO] In-memory cache configured as fallback.");

        if (hasRedisConfig)
        {
            ConfigureRedisCache(services, redisConfig!, logger);
        }
        else
        {
            logger.LogInformation("[INFO] Redis configuration not found. Using in-memory cache only.");
        }

        // HybridCache automatically uses whichever backend is available
        ConfigureHybridCache(services, logger);
        logger.LogInformation("[INFO] Cache configuration completed successfully.");
    }

    private static void ConfigureRedisCache(IServiceCollection services, string redisConfig, ILogger logger)
    {
        try
        {
            logger.LogInformation("[INFO] Configuring Redis cache...");
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConfig;
                // Add some resilience options
                options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConfig);
                options.ConfigurationOptions.ConnectTimeout = 5000; 
                options.ConfigurationOptions.SyncTimeout = 5000;  
                options.ConfigurationOptions.AbortOnConnectFail = false; // Allow fallback
            });
            logger.LogInformation("[INFO] Redis cache configured successfully.");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"[WARNING] Redis configuration failed: {ex.Message}");
            logger.LogInformation("[INFO] Will use in-memory cache as fallback.");
        }
    }

    private static void ConfigureHybridCache(IServiceCollection services, ILogger logger)
    {
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(5)
            };
        });
        logger.LogInformation("[INFO] HybridCache configured successfully.");
    }
}