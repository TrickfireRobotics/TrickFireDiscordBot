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
    private const string InteractionId = "CheckInOutButton";

    private static readonly Random random = new();

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

                // TODO: Those without a nickname will throw an error here if
                // they're signed in between restarts since username isn't
                // serialized with member
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
                InteractionId,
                "Check In or Out"
            ))
            .AddEmbed(embed.Build());
    }

    public Task HandleEventAsync(DiscordClient _, ComponentInteractionCreatedEventArgs e)
    {
        if (e.Id != InteractionId)
        {
            return Task.CompletedTask;
        }

        return CheckInOutInternal(e.Interaction);
    }

    /// <inheritdoc/>
    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddInjectableHostedService<CheckInOutService>();
    }

    internal async Task CheckInOutInternal(DiscordInteraction interaction)
    {
        await interaction.DeferAsync(true);

        // Member is not since this command cannot be called outside of
        // guilds
        DiscordMember member = (interaction.User as DiscordMember)!;

        // Find index of member in list
        int memberIndex = -1;
        for (int i = 0; i < BotState.Members.Count; i++)
        {
            if (BotState.Members[i].member.Id == member.Id)
            {
                memberIndex = i;
                BotState.Members[i] = (member, BotState.Members[i].time);
                break;
            }
        }

        // Update member list
        if (memberIndex == -1)
        {
            BotState.Members.Add((member, interaction.CreationTimestamp));
        }
        else
        {
            BotState.Members.RemoveAt(memberIndex);
        }

        // Send confirmation response
        DiscordFollowupMessageBuilder builder = new() { IsEphemeral = true };
        if (memberIndex == -1)
        {
            builder.WithContent("Checked in. " + GetCheckInMessage());
        }
        else
        {
            builder.WithContent($"Checked out. " + GetCheckOutMessage());
        }
        await interaction.CreateFollowupMessageAsync(builder);
    }

    // Make a fake weighted random using range checking
    /// <summary>
    /// Returns a random checkin message.
    /// </summary>
    /// <returns>a random checkin message</returns>
    private static string GetCheckInMessage()
        => random.Next(100) switch
        {
            < 5 => "Make sure to duck when near the arm!",
            >= 5 and < 15 => "Safety is definitely NOT a suggestion (>ᴗ<)!",
            >= 15 and < 25 => "Do NOT break a leg (there will be too much paperwork)",
            >= 25 and < 35 => "Remember to pray to the old rover when you walk past it!",
            >= 35 and < 40 => "Good luck! (you'll need it)",
            _ => "Welcome to the shop!"
        };

    /// <summary>
    /// Returns a random checkout message.
    /// </summary>
    /// <returns>a random checkout message.</returns>
    private static string GetCheckOutMessage()
        => random.Next(100) switch
        {
            < 5 => "Trickfire is not responsible for any lost or damaged limbs /j",
            >= 5 and < 15 => "You will be paid in exposure soon!",
            >= 15 and < 35 => "Hope you had lots of fun!",
            >= 35 and < 40 => "do i have to sound excited all the time?",
            _ => "Thanks for all the good work!"
        };
}
