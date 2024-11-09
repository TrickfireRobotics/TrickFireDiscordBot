using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text;

namespace TrickfireCheckIn.Discord
{
    /// <summary>
    /// A class representing the Discord bot.
    /// </summary>
    /// <param name="token">The token of the bot</param>
    public class Bot
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

        private bool _needToUpdateEmbed = true;

        public Bot(string token)
        {
            DiscordClientBuilder builder = DiscordClientBuilder
                .CreateDefault(token, DiscordIntents.AllUnprivileged)
                .ConfigureExtraFeatures((conf) =>
                {
                    conf.AbsoluteMessageCacheExpiration = TimeSpan.FromMinutes(5);
                    conf.SlidingMessageCacheExpiration = TimeSpan.FromMinutes(1);
                    conf.AlwaysCacheMembers = false;
                })
                .UseCommands((_, extension) =>
                {
                    // Configure to slash commands
                    extension.AddProcessor(new SlashCommandProcessor());

                    // Add our commands from our code (anything with the command
                    // decorator)
                    extension.AddCommands(Assembly.GetExecutingAssembly());
                })
                .ConfigureEventHandlers(events =>
                {
                    events.HandleComponentInteractionCreated(OnComponentInteraction);
                });
         
            Client = builder.Build();

            // Subscribe to updates of member list
            object lock_ = new();
            State.Members.CollectionChanged += (_, ev) => 
            { 
                lock(lock_)
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
                        string.Join(", ", newItems));
                }
            };
        }

        private static Task OnComponentInteraction(DiscordClient _, ComponentInteractionCreatedEventArgs e)
        {
            if (e.Id != "CheckInOutButton")
            {
                return Task.CompletedTask;
            }

            return Commands.CheckInOutInternal(e.Interaction);
        }

        /// <summary>
        /// Connects the bot and starts it.
        /// </summary>
        public async Task Start()
        {
            // This tells Discord we are using slash commands
            await Client.InitializeAsync();

            // Connect our bot to the Discord API
            await Client.ConnectAsync();

            // Run our long running task to monitor state
            _ = Task.Run(async () => {
                try
                {
                    await LongThread();
                }
                catch (Exception ex)
                {
                    Client.Logger.LogError(ex, "Bot long thread init has exception:");
                }
            });
        }

        private async Task LongThread()
        {
            ulong lastCheckInChannel = Config.Instance.CheckInChannel;
            DateTimeOffset lastClearTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-8));
            while (true)
            {
                // Wait so we're not running at the speed of light
                await Task.Delay(3000);
                try
                {
                    // Clear member list at the start of each day
                    DateTimeOffset currentTime = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-8));
                    if (lastClearTime.Day != currentTime.Day)
                    {
                        State.Members.Clear();
                        lastClearTime = currentTime;
                    }

                    // Check if we're connected to discord yet
                    if (!Client.AllShardsConnected)
                    {
                        continue;
                    }

                    // Update embed to reflect number of members checked in
                    if (_needToUpdateEmbed || lastCheckInChannel != Config.Instance.CheckInChannel)
                    {
                        await UpdateListMessage();
                        lastCheckInChannel = Config.Instance.CheckInChannel;

                        // Update status to reflect number of members checked in
                        if (_needToUpdateEmbed)
                        {
                            await Client.UpdateStatusAsync(new DiscordActivity(
                                $" {State.Members.Count} member{(State.Members.Count == 1 ? "" : "s")} in the shop!",
                                DiscordActivityType.Watching
                            ));
                            _needToUpdateEmbed = false;
                        }
                    }

                }
                catch (Exception ex)
                {
                    Client.Logger.LogError(ex, "Bot long thread has exception:");
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
            DiscordGuild tfGuild = await Client.GetGuildAsync(Config.Instance.TrickfireGuild);
            DiscordChannel channel;
            try
            {
                channel = await tfGuild.GetChannelAsync(Config.Instance.CheckInChannel);
            }
            catch (NotFoundException)
            {
                return;
            }

            try
            {
                // If it does, update it
                DiscordMessage message = await channel.GetMessageAsync(Config.Instance.ListMessage);
                await message.ModifyAsync(builder);
            }
            catch (DiscordException ex)
            {
                if (ex is not NotFoundException && ex is not UnauthorizedException )
                {
                    return;
                }
                // If not, update the config with the new message
                Config.Instance.ListMessage = (await channel.SendMessageAsync(builder)).Id;
                Config.Instance.SaveConfig();
            }

        }

        /// <summary>
        /// Returns an embed listing the members in <see cref="Config.Members"/>.
        /// </summary>
        /// <returns>An embed listing the checked in members</returns>
        private static DiscordMessageBuilder CreateMessage()
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
            for (int i = 0; i < State.Members.Count; i++)
            {
                (DiscordMember member, DateTimeOffset time) = State.Members[i];

                sb.AppendLine($"{member.Mention} ({Formatter.Timestamp(time, TimestampFormat.ShortTime)})");
            }
            
            // Sad no members message :(
            if (State.Members.Count == 0)
            {
                sb.AppendLine("No ones in the shop :(\n" + SadCatASCII);
            }

            // Add description
            embed.WithDescription(sb.ToString());

            return new DiscordMessageBuilder()
                .AddComponents(new DiscordButtonComponent(
                    DiscordButtonStyle.Success,
                    "CheckInOutButton",
                    "Check In or Out"
                ))
                .AddEmbed(embed.Build());
        }
    }
}
