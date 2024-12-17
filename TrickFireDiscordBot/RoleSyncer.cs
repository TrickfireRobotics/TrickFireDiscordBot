using Newtonsoft.Json;
using Notion.Client;
using System.Net;
using TrickFireDiscordBot.Discord;
using TrickFireDiscordBot.Notion;

namespace TrickFireDiscordBot
{
    public class RoleSyncer(NotionClient notionClient, DiscordBot discordBot, WebhookListener listener)
    {
        public const string WebhookEndpoint = "/members";

        public NotionClient NotionClient { get; } = notionClient;
        public DiscordBot DiscordBot { get; } = discordBot;
        public WebhookListener WebhookListener { get; } = listener;

        public void Start()
        {
            WebhookListener.OnWebhookReceived += OnWebhook;
        }

        public void Stop()
        {
            WebhookListener.OnWebhookReceived -= OnWebhook;
        }

        private void OnWebhook(HttpListenerRequest request)
        {
            if (request.RawUrl == null || !request.RawUrl.StartsWith(WebhookEndpoint))
            {
                return;
            }

            using StreamReader reader = new(request.InputStream);
            Automation? automation = JsonConvert.DeserializeObject<Automation>(reader.ReadToEnd());
            if (automation == null || automation.Data is not Page page)
            {
                return;
            }

            Console.WriteLine(JsonConvert.SerializeObject(page, Formatting.Indented));
        }
    }
}
