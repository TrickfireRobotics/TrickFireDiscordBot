using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using System.ComponentModel;

namespace TrickfireCheckIn.Discord
{
    /// <summary>
    /// A class representing commands of the bot.
    /// </summary>
    public static class Commands
    {
        private static readonly Random random = new();

        [Command("setcheckinchannel")]
        [Description("Sets the channel the bot sends the checkin message to")]
        [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
        [RequirePermissions(DiscordPermissions.None, DiscordPermissions.ManageGuild)]
        public static async Task SetCheckInChannel(
            SlashCommandContext context, 
            [Parameter("channel")]
            [Description("The channel to send checkin messages to")]
            DiscordChannel channel
        ) {
            // Guild is not null because it cannot be called outsides guilds
            DiscordPermissions permissions = channel.PermissionsFor(context.Guild!.CurrentMember);
            if (!permissions.HasPermission(DiscordPermissions.SendMessages | DiscordPermissions.AccessChannels))
            {
                await context.RespondAsync("Bot does not have permission to send messages in that channel");
                return;
            }
            else if (!permissions.HasPermission(DiscordPermissions.ReadMessageHistory))

            // Delete old message
            try
            {
                DiscordChannel oldChannel = await context.Guild!.GetChannelAsync(Config.Instance.CheckInChannel);
                DiscordMessage message = await oldChannel.GetMessageAsync(Config.Instance.ListMessage);
                await message.DeleteAsync();
            }
            catch (NotFoundException) { }
            catch (UnauthorizedException) { }

            // Update channel in config
            Config.Instance.CheckInChannel = channel.Id;
            Config.Instance.ListMessage = 0;
            Config.Instance.SaveConfig();

            // Return success
            await context.RespondAsync("Channel succesfully set!");
        }

        [Command("ping")]
        [Description("Pings the bot to make sure it's not dead")]
        [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
        public static ValueTask Ping(SlashCommandContext context)
        {
            // Guild is not null because it cannot be called outsides guilds
            TimeSpan latency = context.Client.GetConnectionLatency(context.Guild!.Id);

            // Ephemeral makes it so the response is invisible to other people
            return context.RespondAsync($"Pong! Latency is {latency.TotalMilliseconds:N0}ms", true);
        }
            

        [Command("checkinout")]
        [Description("Checks you into/out of the shop and updates the member list")]
        [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
        public static Task CheckInOut(SlashCommandContext context)
        {
            return CheckInOutInternal(context.Interaction);
        }

        internal static async Task CheckInOutInternal(DiscordInteraction interaction)
        {
            await interaction.DeferAsync(true);

            // Member is not since this command cannot be called outside of
            // guilds
            DiscordMember member = (interaction.User as DiscordMember)!;

            // Find index of member in list
            int memberIndex = -1;
            for (int i = 0; i < State.Members.Count; i++)
            {
                if (State.Members[i].member == member)
                {
                    memberIndex = i;
                    break;
                }
            }

            // Update member list
            if (memberIndex == -1)
            {
                State.Members.Add((member, interaction.CreationTimestamp));
            }
            else
            {
                State.Members.RemoveAt(memberIndex);
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
}