using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TrickFireDiscordBot.Discord;

namespace TrickFireDiscordBot
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            string[] lines;
            if (args.Length == 1)
            {
                lines = File.ReadAllLines(args[0]);
            }
            else
            {
                lines = File.ReadAllLines("secrets.txt");
            }

            try
            {
                ServiceCollection services = new();

                services
                    // Add notion client
                    .AddNotionClient(options =>
                    {
                        options.AuthToken = lines[1];
                    })

                    // Add webhook listener
                    .AddSingleton(container =>
                    {
                        // This is not good security practice, but fly.io requires us to 
                        // expose 0.0.0.0:8080, which this is equivalent to
                        WebhookListener webhookListener = new(container.GetRequiredService<ILogger<WebhookListener>>(), "http://*:8080/");
                        webhookListener.Start();
                        return webhookListener;
                    })

                    // Add role syncer
                    .AddSingleton<RoleSyncer>()

                    .AddSingleton<BotState>();

                // Start the bot
                DiscordBot bot = new(lines[0], services);
                await bot.Start();

                // Start the role syncer
                await bot.Client.ServiceProvider.GetRequiredService<RoleSyncer>().Start(bot.Client);
            }
            catch (Exception e)
            {
                Console.WriteLine("Startup error. The bot is likely not in a working state: " + e.ToString());
            }

            // Hang the process forever so it doesn't quit after the bot
            // connects
            await Task.Delay(-1);
        }
    }
}
