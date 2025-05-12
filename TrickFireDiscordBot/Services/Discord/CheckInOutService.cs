using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace TrickFireDiscordBot.Services.Discord;

public class CheckInOutService 
    : BackgroundService, IAutoRegisteredService, IEventHandler<ComponentInteractionCreatedEventArgs>
{
    private const string SadCatASCII =
        "　　　　   ／＞----フ\r\n" +
        "　　　　 　|　\\_  \\_ l\r\n" +
        "　 　　　／` ミ＿xノ　\r\n" +
        "　　　　/　　　　 |\r\n" +
        "　　　 /　ヽ　   ﾉ\r\n" +
        "　　　│　　|　|　|\r\n" +
        "　／￣|　　 |　|　|\r\n" +
        "　| (￣ヽ＿\\_ヽ\\_)\\_\\_)\r\n" +
        "　＼二つ";
    private BotState BotState { get; }
    private DiscordService Discord { get; }

    private bool _needToUpdateEmbed = true;

    public CheckInOutService(BotState botState, DiscordService discord)
    {
        BotState = botState;
        Discord = discord;

        // Subscribe to updates of member list
        object lock_ = new();
        BotState.Members.CollectionChanged += (_, ev) =>
        {
            lock (lock_)
            {
                _needToUpdateEmbed = true;

                IEnumerable<string> oldItems = (ev.OldItems ?? new List<string>())
                    .Cast<(DiscordMember, DateTimeOffset)>()
                    .Select(val => val.Item1.DisplayName);

                IEnumerable<string> newItems = (ev.NewItems ?? new List<string>())
                    .Cast<(DiscordMember, DateTimeOffset)>()
                    .Select(val => val.Item1.DisplayName);

                Discord.Client.Logger.LogInformation(
                    "Member collection changed: {}\nOld items: {}\nNew items: {}",
                    ev.Action.ToString(),
                    string.Join(", ", oldItems),
                    string.Join(", ", newItems)
                );
            }
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ulong lastCheckInChannel = BotState.CheckInChannelId;
        DateTimeOffset lastClearTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-8));
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait so we're not running at the speed of light
            await Task.Delay(3000, stoppingToken);
            try
            {
                // Clear member list at the start of each day
                DateTimeOffset currentTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-8));
                if (lastClearTime.Day != currentTime.Day)
                {
                    BotState.Members.Clear();
                    lastClearTime = currentTime;
                }

                // Check if we're connected to discord yet
                if (!Discord.Client.AllShardsConnected)
                {
                    continue;
                }

                // Update embed to reflect number of members checked in
                if (_needToUpdateEmbed || lastCheckInChannel != BotState.CheckInChannelId || BotState.ListMessageId == 0)
                {
                    await UpdateListMessage();
                    lastCheckInChannel = BotState.CheckInChannelId;

                    // Update status to reflect number of members checked in
                    if (_needToUpdateEmbed)
                    {
                        await Discord.Client.UpdateStatusAsync(new DiscordActivity(
                            $" {BotState.Members.Count} member{(BotState.Members.Count == 1 ? "" : "s")} in the shop!",
                            DiscordActivityType.Watching
                        ));
                        _needToUpdateEmbed = false;
                    }
                }

            }
            catch (Exception ex)
            {
                Discord.Client.Logger.LogError(ex, "Bot main loop:");
            }
        }
    }

    /// <summary>
    /// Resends or updates the list message if it doesn't exist
    /// </summary>
    private async Task UpdateListMessage()
    {
        DiscordMessageBuilder builder = CreateMessage();

        // Check if message exists
        DiscordChannel channel;
        try
        {
            channel = await Discord.MainGuild.GetChannelAsync(BotState.CheckInChannelId);
        }
        catch (NotFoundException)
        {
            return;
        }

        try
        {
            // If it does, update it
            DiscordMessage message = await channel.GetMessageAsync(BotState.ListMessageId);
            await message.ModifyAsync(builder);
        }
        catch (DiscordException ex)
        {
            if (ex is not NotFoundException && ex is not UnauthorizedException)
            {
                return;
            }
            // If not, update the config with the new message
            BotState.ListMessageId = (await channel.SendMessageAsync(builder)).Id;
            BotState.Save();
        }

    }

    /// <summary>
    /// Returns an embed listing the members in <see cref="Config.Members"/>.
    /// </summary>
    /// <returns>An embed listing the checked in members</returns>
    private DiscordMessageBuilder CreateMessage()
    {
        // Create embed without members
        DiscordEmbedBuilder embed = new DiscordEmbedBuilder()
            .WithTitle("Members in the Shop")
            .WithFooter("Made by Kyler 😎")
            .WithTimestamp(DateTime.Now)
            .WithColor(new DiscordColor("2ecc71"));

        StringBuilder sb = new(
            "A list of members currently in the shop (INV-011), kept up to date.\n" +
            "Check in or out using the button or `/checkinout` command!\n\n"
        );

        // Add members to description string
        for (int i = 0; i < BotState.Members.Count; i++)
        {
            (DiscordMember member, DateTimeOffset time) = BotState.Members[i];

            sb.AppendLine($"{member.Mention} ({Formatter.Timestamp(time, TimestampFormat.ShortTime)})");
        }

        // Sad no members message :(
        if (BotState.Members.Count == 0)
        {
            sb.AppendLine("No one's in the shop :(\n" + SadCatASCII);
        }

        // Add description
        embed.WithDescription(sb.ToString());

        return new DiscordMessageBuilder()
            .AddActionRowComponent(new DiscordButtonComponent(
                DiscordButtonStyle.Success,
                "CheckInOutButton",
                "Check In or Out"
            ))
            .AddEmbed(embed.Build());
    }

    /// <inheritdoc/>
    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddInjectableHostedService<CheckInOutService>();
    }

    public Task HandleEventAsync(DiscordClient _, ComponentInteractionCreatedEventArgs e)
    {
        if (e.Id != "CheckInOutButton")
        {
            return Task.CompletedTask;
        }

        return Commands.CheckInOutInternal(e.Interaction, BotState);
    }
}
