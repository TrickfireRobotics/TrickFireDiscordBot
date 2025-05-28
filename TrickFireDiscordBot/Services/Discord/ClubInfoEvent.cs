using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TrickFireDiscordBot.Services.Discord;

public class ClubInfoEvent(IOptions<ClubInfoEventOptions> options, BotState botState, DiscordService discord) 
    : IHostedService, IAutoRegisteredService
{
    private readonly DiscordMessageBuilder message = new DiscordMessageBuilder()
        .EnableV2Components()
        .AddContainerComponent(new DiscordContainerComponent([
            new DiscordMediaGalleryComponent(
                new DiscordMediaGalleryItem(options.Value.ClubInfoLogoUrl)
            ),
            new DiscordTextDisplayComponent(options.Value.ClubInfoMessage),
            new DiscordSeparatorComponent(divider: true),
            new DiscordTextDisplayComponent("Below are some nifty links for everyone"),
            new DiscordActionRowComponent([
                new DiscordLinkButtonComponent("https://www.notion.so/trickfire/invite/d3549ba6387d94a9454679a4082d848706d1dd29", "Notion"),
                new DiscordLinkButtonComponent("https://schej.it/e/7aDA2", "Schej"),
                new DiscordLinkButtonComponent("https://www.notion.so/trickfire/1301fd41ff5b81059fc6e6461d7bb25b?v=1301fd41ff5b813487ee000c80faa461", "Calendar")
            ]),
            new DiscordActionRowComponent([
                new DiscordLinkButtonComponent("https://www.notion.so/trickfire/1301fd41ff5b81f28a91f837f0ea28a4?v=1301fd41ff5b81ba9807000c532159af", "Teams"),
                new DiscordLinkButtonComponent("https://www.notion.so/trickfire/1481fd41ff5b80dd8b9df498ae5dbd6e?v=1481fd41ff5b8186b948000c5531b0c0", "Wiki"),
                new DiscordLinkButtonComponent("https://www.notion.so/trickfire/1eb1fd41ff5b80b7a96adeaa5aea7655", "Task Creator")
            ])
        ], color: new DiscordColor("19a24a")));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (discord.MainGuild.RulesChannelId == 0 || discord.MainGuild.RulesChannelId is null)
        {
            return;
        }

        DiscordChannel channel = await discord.MainGuild.GetChannelAsync(discord.MainGuild.RulesChannelId.Value);
        
        // Update message
        if (botState.ClubInfoMessageId != 0)
        {
            try
            {
                DiscordMessage oldMessage = await channel.GetMessageAsync(botState.ClubInfoMessageId);
                await oldMessage.ModifyAsync(message);
                return;
            }
            catch (DiscordException ex)
            {
                // If not found, then resent
                if (ex is not NotFoundException)
                {
                    throw;
                }
            }

        }

        DiscordMessage sentMessage = await channel.SendMessageAsync(message);
        await Task.Delay(3000, cancellationToken);
        botState.ClubInfoMessageId = sentMessage.Id;
        botState.Save();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddHostedService<ClubInfoEvent>()
            .ConfigureTypeSection<ClubInfoEventOptions>(builder.Configuration);
    }
}

public class ClubInfoEventOptions
{
    /// <summary>
    /// The url of the logo on the message.
    /// </summary>
    public string ClubInfoLogoUrl { get; set; } = "";

    /// <summary>
    /// The message content of the message.
    /// </summary>
    public string ClubInfoMessage { get; set; } = "";
}