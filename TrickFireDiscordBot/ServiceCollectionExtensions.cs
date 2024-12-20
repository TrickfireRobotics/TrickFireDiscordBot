using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TrickFireDiscordBot
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInjectableHostedService<T>(this IServiceCollection services) where T : class, IHostedService
            => services.AddSingleton<T>().AddHostedService(provider => provider.GetRequiredService<T>());

        public static IServiceCollection ConfigureTypeSection<T>(this IServiceCollection services, IConfiguration config) where T : class
            => services.Configure<T>(config.GetSection(typeof(T).Name));
    }
}
