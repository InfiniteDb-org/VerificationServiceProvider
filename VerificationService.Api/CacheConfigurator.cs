using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace VerificationService.Api;

public static class CacheConfigurator
{
    public static void Configure(IServiceCollection services, IConfiguration configuration, ILogger logger)
    {
        var redisConfig = Environment.GetEnvironmentVariable("Redis__Configuration")
                          ?? configuration["Redis__Configuration"];
        var hasRedisConfig = !string.IsNullOrEmpty(redisConfig);
        
        logger.LogInformation("Redis config available: {HasRedisConfig}", hasRedisConfig);

        // Always add MemoryCache first as guaranteed fallback
        services.AddMemoryCache();
        logger.LogInformation("In-memory cache configured as fallback");

        // Try to add Redis if config is available
        if (hasRedisConfig)
        {
            var redisWorking = TryConfigureRedis(services, redisConfig!, logger);
            if (redisWorking)
            {
                logger.LogInformation("Redis cache configured successfully");
            }
            else
            {
                logger.LogWarning("⚠Redis failed, will use in-memory cache only");
            }
        }
        else
        {
            logger.LogInformation("No Redis config found, using in-memory cache only");
        }

        // Configure HybridCache (will use Redis + Memory if available, or just Memory)
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1) 
            };
        });
        
        logger.LogInformation("HybridCache configured successfully");
    }

    private static bool TryConfigureRedis(IServiceCollection services, string redisConfig, ILogger logger)
    {
        try
        {
            logger.LogInformation("Attempting Redis configuration...");
            
            // Parse and validate Redis config first
            var configOptions = ConfigurationOptions.Parse(redisConfig);
            
            // Optimize settings for Azure Functions
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 2;
            configOptions.KeepAlive = 60;
            
            // Health check inställningar
            configOptions.HeartbeatInterval = TimeSpan.FromSeconds(30);
            configOptions.HeartbeatConsistencyChecks = false;
            
            services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = configOptions;
                options.InstanceName = "VerificationService";
            });
            
            logger.LogInformation("Redis configuration added to services");
            return true;
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "Invalid Redis connection string format");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure Redis cache");
            return false;
        }
    }
}