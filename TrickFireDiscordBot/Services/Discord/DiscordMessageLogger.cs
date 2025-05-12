using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Threading.Channels;

namespace TrickFireDiscordBot.Services.Discord;

public class DiscordMessageLogger(
    //IOptions<DiscordMessageLoggerOptions> options,
    ILogger<DiscordMessageLogger> logger,
    BotState botState,
    DiscordService discordService) : BackgroundService, IAutoRegisteredService, ILogger
{
    private readonly Channel<string> queue = Channel.CreateBounded<string>(100);
    private readonly StringBuilder sb = new();

    private DiscordChannel? channel;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = $"[{eventId.Id,2}: {logLevel,-12}] {formatter(state, exception)}";
        if (message.Length < 2000)
        {
            queue.Writer.TryWrite(message);
            return;
        }

        // Split log message into 2000 char chunks
        for (int i = 0; i < message.Length; i += 2000)
        {
            queue.Writer.TryWrite(message[i..(i + 2000)]);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Dequeue all items from queue
                // cts will be cancelled after 3 seconds
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(3000);
                while (!cts.IsCancellationRequested)
                {
                    // Wait for item to be available
                    try
                    {
                        await queue.Reader.WaitToReadAsync(cts.Token);
                    }
                    catch (OperationCanceledException) 
                    {
                        break;
                    }
                    
                    // Peek the item
                    if (!queue.Reader.TryPeek(out string? message) || message == null)
                    {
                        continue;
                    }
                    
                    // Make sure it fits within our message, if it doesn't then
                    // send message
                    if (sb.Length + 1 + message.Length > 2000)
                    {
                        // Ensure the timeout is finished
                        try
                        {
                            await Task.Delay(-1, cts.Token);
                        }
                        catch (OperationCanceledException) { }
                        break;
                    }

                    // Add item to our message
                    sb.AppendLine(message);
                    queue.Reader.TryRead(out message);
                }

                // Keep waiting if no messages were enqueued
                if (sb.Length == 0)
                {
                    continue;
                }

                // Make sure channel is not null
                DiscordChannel? channel = await GetChannel();
                if (channel is null)
                {
                    continue;
                }

                // Send message
                await channel.SendMessageAsync(sb.ToString());
                sb.Clear();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Main loop errored:");
            }
        }
    }

    private async Task<DiscordChannel?> GetChannel()
    {
        if (channel is null || channel.Id != botState.MessageLoggerChannelId)
        {
            try
            {
                channel = await discordService.MainGuild.GetChannelAsync(botState.MessageLoggerChannelId);
            }
            catch (DiscordException ex)
            {
                logger.LogError(ex, "Could not find channel: {}", botState.MessageLoggerChannelId);
            }
        }
        return channel;
    }

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddInjectableHostedService<DiscordMessageLogger>();
    }
}
