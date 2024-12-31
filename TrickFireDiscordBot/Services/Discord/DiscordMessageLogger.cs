using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace TrickFireDiscordBot.Services.Discord;

public class DiscordMessageLogger(
    //IOptions<DiscordMessageLoggerOptions> options,
    ILogger<DiscordMessageLogger> logger,
    BotState botState,
    DiscordService discordService) : BackgroundService, IAutoRegisteredService, ILogger
{
    private readonly Channel<string> queue = Channel.CreateBounded<string>(100);

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

        queue.Writer.TryWrite($"[{eventId.Id,2}: {logLevel,-12}] {formatter(state, exception)}");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string message = await queue.Reader.ReadAsync(stoppingToken);

                DiscordChannel? channel = await GetChannel();
                if (channel is null)
                {
                    continue;
                }

                await channel.SendMessageAsync(message);
                await Task.Delay(3000, stoppingToken);
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
            .AddInjectableHostedService<DiscordMessageLogger>()
            .ConfigureTypeSection<DiscordMessageLoggerOptions>(builder.Configuration);
    }
}

public class DiscordMessageLoggerOptions
{
}
