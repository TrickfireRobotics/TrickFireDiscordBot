using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Notion.Client;
using System.Net;
using System.Threading.Channels;

namespace TrickFireDiscordBot.Services.Notion;

/// <summary>
/// A class that represents a service that receives Notion Pages through a 
/// webhook. Notion pages are added to a channel and can later be processed by
/// the <see cref="BackgroundService.ExecuteAsync(CancellationToken)"/> method.
/// <typeparam name="QueueT">The type of the <see cref="PageQueue"/></typeparam>
/// <param name="logger">The logger for this service</param>
/// <param name="webhookListener">The webhook listener for this service</param>
public abstract class NotionWebhookService<QueueT>(ILogger logger, WebhookListener webhookListener) : BackgroundService
{
    /// <summary>
    /// The endpoint of the webhook, including the leading `/`.
    /// </summary>
    public abstract string WebhookEndpoint { get; }

    /// <summary>
    /// The queue that pages are added to when a webhook is received.
    /// 
    /// Defaults to an unbounded channel.
    /// </summary>
    protected virtual Channel<QueueT> PageQueue { get; } = Channel.CreateUnbounded<QueueT>();

    protected WebhookListener WebhookListener => webhookListener;

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        webhookListener.OnWebhookReceived += OnWebhook;

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        webhookListener.OnWebhookReceived -= OnWebhook;
    }

    private void OnWebhook(HttpListenerRequest request)
    {
        if (request.RawUrl != WebhookEndpoint)
        {
            return;
        }

        using StreamReader reader = new(request.InputStream);
        string json = reader.ReadToEnd();
        Automation? automation = JsonConvert.DeserializeObject<Automation>(json);
        if (automation == null || automation.Data is not Page page)
        {
            logger.LogWarning("Could not parse automation: {}", json);
            return;
        }

        OnPageWebhook(page);
    }

    /// <summary>
    /// A method that's called whenever a webhook is received with a valid page.
    /// </summary>
    /// <param name="notionPage">The page that was received</param>
    protected abstract void OnPageWebhook(Page notionPage);
}
