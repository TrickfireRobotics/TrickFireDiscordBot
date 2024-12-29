using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace TrickFireDiscordBot.Services.Discord;

public class DiscordMessageLogger(
    IOptions<DiscordMessageLoggerOptions> options,
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

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        channel = await discordService.MainGuild.GetChannelAsync(options.Value.ChannelId);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string message = await queue.Reader.ReadAsync(stoppingToken);
                if (channel is null)
                {
                    continue;
                }

                await channel.SendMessageAsync(message);
                await Task.Delay(3000, stoppingToken);
            }
            catch
            {
                // bad stuff happens
            }
        }
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
    public ulong ChannelId { get; set; } = 0;
}
