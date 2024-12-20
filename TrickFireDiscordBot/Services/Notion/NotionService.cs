using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TrickFireDiscordBot.Services.Notion
{
    public class NotionService : IAutoRegisteredService
    {
        public static void Register(IHostApplicationBuilder builder)
        {
            builder.Services
                .AddNotionClient(options =>
                {
                    options.AuthToken = builder.Configuration["NOTION_SECRET"];
                });
        }
    }
}
