using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VerificationService.Api
{
    public static class CacheConfigurator
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration, ILogger logger)
        {
            var redisConfig = Environment.GetEnvironmentVariable("Redis__Configuration")
                              ?? configuration["Redis__Configuration"];
            
            var hasRedisConfig = !string.IsNullOrEmpty(redisConfig);
            logger.LogInformation($"[DEBUG] Redis config available: {hasRedisConfig}");

            if (hasRedisConfig)
            {
                ConfigureRedisCache(services, redisConfig!, logger);
            }
            else
            {
                ConfigureInMemoryCache(services, logger);
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
                    options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConfig);
                    options.ConfigurationOptions.ConnectTimeout = 5000;
                    options.ConfigurationOptions.SyncTimeout = 5000;
                    options.ConfigurationOptions.AbortOnConnectFail = false;
                });
                logger.LogInformation("[INFO] Redis cache configured successfully.");
            }
            catch (Exception ex)
            {
                logger.LogWarning($"[WARNING] Redis configuration failed: {ex.Message}");
                logger.LogInformation("[INFO] Falling back to in-memory cache...");
                ConfigureInMemoryCache(services, logger);
            }
        }

        private static void ConfigureInMemoryCache(IServiceCollection services, ILogger logger)
        {
            logger.LogInformation("[INFO] Configuring in-memory cache...");
            services.AddMemoryCache();
            logger.LogInformation("[INFO] In-memory cache configured successfully.");
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
}
