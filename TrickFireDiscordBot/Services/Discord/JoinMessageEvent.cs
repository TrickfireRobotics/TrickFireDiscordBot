using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TrickFireDiscordBot.Services.Discord;

public class JoinMessageEvent(IOptions<JoinMessageEventOptions> options)
    : IEventHandler<GuildMemberAddedEventArgs>, IAutoRegisteredService
{
    private DiscordChannel? _welcomeChannel = null;

    /// <inheritdoc/>
    public async Task HandleEventAsync(DiscordClient sender, GuildMemberAddedEventArgs eventArgs)
    {
        // Skip if no channel set, else set welcome channel cache
        if (eventArgs.Guild.SystemChannelId == null)
        {
            return;
        }

        _welcomeChannel ??= await eventArgs.Guild.GetChannelAsync(eventArgs.Guild.SystemChannelId.Value);

        // Send join message
        try
        {
            string content = string.Format(options.Value.JoinMessage, eventArgs.Member.Mention);
            DiscordMessage message = await _welcomeChannel.SendMessageAsync(content);
            await Task.Delay(1000);
            await message.ModifyEmbedSuppressionAsync(true);
        }
        catch (Exception ex)
        {
            sender.Logger.LogError(ex, "Failed to send welcome message:");
        }
    }

    /// <inheritdoc/>
    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .ConfigureTypeSection<JoinMessageEventOptions>(builder.Configuration);
    }
}

public class JoinMessageEventOptions
{
    /// <summary>
    /// The join message sent in the server whenever a user joins.
    /// 
    /// `{0}` can be used to place the user's ping in place of it.
    /// </summary>
    public string JoinMessage { get; set; } = "";
}

