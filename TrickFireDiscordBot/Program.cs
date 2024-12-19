using Notion.Client;
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
                // Start the bot
                DiscordBot bot = new(lines[0]);
                await bot.Start();

                // Start notion client
                NotionClient notionClient = NotionClientFactory.Create(new ClientOptions()
                {
                    AuthToken = lines[1]
                });

                // Start the webhook listener

                // This is not good security practice, but fly.io requires us to 
                // expose 0.0.0.0:8080, which this is equivalent to
                WebhookListener webhookListener = new(bot.Client.Logger, "http://*:8080/");
                webhookListener.Start();

                // Start role syncer
                RoleSyncer syncer = new(bot.Client.Logger, notionClient, webhookListener);
                await syncer.Start(bot.Client);
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
