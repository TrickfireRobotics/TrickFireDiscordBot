using Microsoft.Extensions.Hosting;

namespace TrickFireDiscordBot.Services
{
    public interface IAutoRegisteredService
    {
        public static abstract void Register(IHostApplicationBuilder builder);
    }
}
