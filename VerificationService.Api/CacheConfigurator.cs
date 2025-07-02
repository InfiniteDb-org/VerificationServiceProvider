using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VerificationService.Api
{
    public static class CacheConfigurator
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            var redisConfig = Environment.GetEnvironmentVariable("Redis__Configuration")
                              ?? configuration["Redis__Configuration"];
            Console.WriteLine($"[DEBUG] Redis config available: {!string.IsNullOrEmpty(redisConfig)}");

            if (string.IsNullOrEmpty(redisConfig))
            {
                Console.WriteLine("[INFO] Redis configuration not found. Using in-memory cache.");
                AddInMemoryCache(services);
                return;
            }

            // Try to configure and test Redis
            if (TryConfigureRedis(services, redisConfig))
            {
                AddHybridCacheWithRedis(services);
                Console.WriteLine("[INFO] Redis cache configured and tested successfully.");
            }
            else
            {
                RemoveRedisServices(services);
                AddInMemoryCache(services);
                Console.WriteLine("[INFO] Falling back to in-memory cache due to Redis issues.");
            }
        }

        private static bool TryConfigureRedis(IServiceCollection services, string redisConfig)
        {
            try
            {
                Console.WriteLine("[INFO] Configuring Redis cache...");
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConfig;
                });
                // Test Redis connection synchronously
                using var serviceProvider = services.BuildServiceProvider();
                var distributedCache = serviceProvider.GetService<IDistributedCache>();
                if (distributedCache == null)
                {
                    Console.WriteLine("[WARNING] IDistributedCache service not available.");
                    return false;
                }
                var testKey = $"healthcheck:{Environment.MachineName}:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                var testValue = "redis-health-check";
                Console.WriteLine("[INFO] Testing Redis connection...");
                distributedCache.SetString(testKey, testValue, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
                });
                var retrievedValue = distributedCache.GetString(testKey);
                if (retrievedValue != testValue)
                {
                    Console.WriteLine("[WARNING] Redis write/read test failed - values don't match.");
                    return false;
                }
                distributedCache.Remove(testKey);
                Console.WriteLine("[INFO] Redis health check passed.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Redis configuration failed: {ex.Message}");
                return false;
            }
        }

        private static void RemoveRedisServices(IServiceCollection services)
        {
            var redisServices = services
                .Where(x => x.ServiceType.FullName?.Contains("Redis") == true ||
                           x.ServiceType.FullName?.Contains("StackExchange") == true)
                .ToList();
            foreach (var service in redisServices)
            {
                services.Remove(service);
            }
        }

        private static void AddHybridCacheWithRedis(IServiceCollection services)
        {
            services.AddHybridCache(options =>
            {
                options.DefaultEntryOptions = new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(5),
                    LocalCacheExpiration = TimeSpan.FromMinutes(5)
                };
            });
        }

        private static void AddInMemoryCache(IServiceCollection services)
        {
            Console.WriteLine("[INFO] Configuring in-memory cache...");
            services.AddMemoryCache();
            services.AddHybridCache(options =>
            {
                options.DefaultEntryOptions = new HybridCacheEntryOptions
                {
                    Expiration = TimeSpan.FromMinutes(5),
                    LocalCacheExpiration = TimeSpan.FromMinutes(5)
                };
            });
            Console.WriteLine("[INFO] In-memory cache configured successfully.");
        }
    }
}
