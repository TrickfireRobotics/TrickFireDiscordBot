using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using DSharpPlus.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text;

namespace TrickFireDiscordBot.Services.Discord;

/// <summary>
/// A class representing the Discord bot.
/// </summary>
/// <param name="token">The token of the bot</param>
public class DiscordService : BackgroundService, IAutoRegisteredService
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

    /// <summary>
    /// The client associated with the bot.
    /// </summary>
    public DiscordClient Client { get; }

    public DiscordServiceOptions Options { get; }
    public DiscordGuild MainGuild { get; }

    private BotState BotState => Client.ServiceProvider.GetRequiredService<BotState>();

    private DiscordChannel? _welcomeChannel = null;
    private bool _needToUpdateEmbed = true;

    public DiscordService(DiscordClient client, IOptions<DiscordServiceOptions> options)
    {
        Client = client;
        Options = options.Value;
        MainGuild = client.GetGuildAsync(options.Value.MainGuildId).GetAwaiter().GetResult();

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

                Client.Logger.LogInformation(
                    "Member collection changed: {}\nOld items: {}\nNew items: {}",
                    ev.Action.ToString(),
                    string.Join(", ", oldItems),
                    string.Join(", ", newItems)
                );
            }
        };
    }


    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // This tells Discord we are using slash commands
        await Client.InitializeAsync();

        // Connect our bot to the Discord API
        await Client.ConnectAsync();

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (Client.AllShardsConnected)
        {
            await Client.DisconnectAsync();
        }
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
                if (!Client.AllShardsConnected)
                {
                    continue;
                }

                // Update embed to reflect number of members checked in
                if (_needToUpdateEmbed || lastCheckInChannel != BotState.CheckInChannelId)
                {
                    await UpdateListMessage();
                    lastCheckInChannel = BotState.CheckInChannelId;

                    // Update status to reflect number of members checked in
                    if (_needToUpdateEmbed)
                    {
                        await Client.UpdateStatusAsync(new DiscordActivity(
                            $" {BotState.Members.Count} member{(BotState.Members.Count == 1 ? "" : "s")} in the shop!",
                            DiscordActivityType.Watching
                        ));
                        _needToUpdateEmbed = false;
                    }
                }

            }
            catch (Exception ex)
            {
                Client.Logger.LogError(ex, "Bot main loop:");
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
            channel = await MainGuild.GetChannelAsync(BotState.CheckInChannelId);
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

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddDiscordClient(builder.Configuration["BOT_TOKEN"]!, DiscordIntents.GuildMembers)
            .Configure<DiscordConfiguration>(builder.Configuration.GetSection("DiscordBotConfig"))
            .AddCommandsExtension((_, extension) =>
            {
                // Configure to slash commands
                extension.AddProcessor(new SlashCommandProcessor());

                // Add our commands from our code (anything with the command
                // decorator)
                extension.AddCommands(Assembly.GetExecutingAssembly());
            })
            .ConfigureEventHandlers(events =>
            {
                events.AddEventHandlers<EventHandlers>();
            })
            .AddInjectableHostedService<DiscordService>()
            .ConfigureTypeSection<DiscordServiceOptions>(builder.Configuration);
    }

    private class EventHandlers(BotState botState, DiscordService service)
        : IEventHandler<ComponentInteractionCreatedEventArgs>, 
          IEventHandler<SessionCreatedEventArgs>, IEventHandler<GuildMemberAddedEventArgs>
    {
        public Task HandleEventAsync(DiscordClient _, ComponentInteractionCreatedEventArgs e)
        {
            if (e.Id != "CheckInOutButton")
            {
                return Task.CompletedTask;
            }

            return Commands.CheckInOutInternal(e.Interaction, botState);
        }

        public async Task HandleEventAsync(DiscordClient sender, SessionCreatedEventArgs eventArgs)
        {
            // Connecting changes the guild in the cache, so reset it to the one
            // we like
            (service.Client.Guilds as IDictionary<ulong, DiscordGuild>)![service.MainGuild.Id] = service.MainGuild;

            // Make sure CurrentMember is not null
            FieldInfo memberField = typeof(DiscordGuild).GetField("members", BindingFlags.Instance | BindingFlags.NonPublic)!;
            IDictionary<ulong, DiscordMember> members = (memberField.GetValue(service.MainGuild) as IDictionary<ulong, DiscordMember>)!;
            members[service.Client.CurrentUser.Id] = await service.MainGuild.GetMemberAsync(service.Client.CurrentUser.Id);
        }

        public async Task HandleEventAsync(DiscordClient sender, GuildMemberAddedEventArgs eventArgs)
        {
            // Skip if no channel set, else set welcome channel cache
            if (eventArgs.Guild.SystemChannelId == null)
            {
                return;
            }

            service._welcomeChannel ??= await eventArgs.Guild.GetChannelAsync(eventArgs.Guild.SystemChannelId.Value);

            // Send join message
            try
            {
                string content = string.Format(service.Options.JoinMessage, eventArgs.Member.Mention);
                DiscordMessage message = await service._welcomeChannel.SendMessageAsync(content);
                await Task.Delay(1000);
                await message.ModifyEmbedSuppressionAsync(true);
            }
            catch (Exception ex)
            {
                sender.Logger.LogError(ex, "Failed to send welcome message:");   
            }
        }
    }
}

public class DiscordServiceOptions
{
    /// <summary>
    /// The id of the main discord guild of the bot.
    /// </summary>
    public ulong MainGuildId { get; set; } = 0;

    /// <summary>
    /// The join message sent in the server whenever a user joins.
    /// 
    /// `{0}` can be used to place the user's ping in place of it.
    /// </summary>
    public string JoinMessage { get; set; } = "";
}
