using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.Metadata;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace TrickFireDiscordBot.Services.Discord;

/// <summary>
/// A class representing commands of the bot.
/// </summary>
public static class Commands
{
    private static readonly Random random = new();

    [Command("setcheckinchannel")]
    [Description("Sets the channel the bot sends the checkin message to")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public static async Task SetCheckInChannel(
        SlashCommandContext context,
        [Parameter("channel")]
        [Description("The channel to send checkin messages to")]
        DiscordChannel channel,
        [Parameter("messageid")]
        [Description("The message id to use as the checkin message")]
        ulong? messageId = null)
    {
        // Guild is not null because it cannot be called outsides guilds
        DiscordPermissions permissions = channel.PermissionsFor(context.Guild!.CurrentMember);
        if (!permissions.HasPermission(DiscordPermission.SendMessages) || !permissions.HasPermission(DiscordPermission.ViewChannel))
        {
            await context.RespondAsync("Bot does not have permission to send messages in that channel");
            return;
        }
        else if (!permissions.HasPermission(DiscordPermission.ReadMessageHistory))
        {
            await context.RespondAsync("Bot does not have permission to read messages in that channel");
            return;
        }

        // Validate message
        DiscordMessage? message = null;
        if (messageId is not null)
        {
            // Try to fetch message
            try
            {
                message = await channel.GetMessageAsync(messageId.Value);
            }
            catch (NotFoundException) { }
            catch (UnauthorizedException) 
            {
                await context.RespondAsync("Improper permissions to get message");
            }

            if (message is null)
            {
                await context.RespondAsync("Message not found");
                return;
            }

            // Fail if bot isn't message author or message channel doesn't match
            // passed in channel
            if (message.Author?.Id != context.Client.CurrentUser.Id)
            {
                await context.RespondAsync("Message must have been sent by bot");
                return;
            }
            else if (message.ChannelId != channel.Id)
            {
                await context.RespondAsync("Message must be sent in same channel as inputted");
                return;
            }
        }

        // Delete old message
        BotState state = context.ServiceProvider.GetRequiredService<BotState>();
        try
        {
            if (message is null || message.Id != state.ListMessageId)
            {
                DiscordChannel oldChannel = await context.Guild!.GetChannelAsync(state.CheckInChannelId);
                DiscordMessage oldMessage = await oldChannel.GetMessageAsync(state.ListMessageId);
                await oldMessage.DeleteAsync();
            }
        }
        catch (NotFoundException) { }
        catch (UnauthorizedException) { }

        // Update settings in config
        if (state.CheckInChannelId != channel.Id)
        {
            state.CheckInChannelId = channel.Id;
        }
        if (message is not null && state.ListMessageId != message.Id)
        {
            state.ListMessageId = message.Id;
        }
        else if (message is null)
        {
            state.ListMessageId = 0;
        }
        state.Save();

        // Return success
        await context.RespondAsync("Channel succesfully set!");
    }

    [Command("setloggingchannel")]
    [Description("Sets the channel the bot sends debug logging messages to")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public static async Task SetLoggingChannel(
        SlashCommandContext context,
        [Parameter("channel")]
        [Description("The channel to send debug messages to")]
        DiscordChannel channel)
    {
        // Guild is not null because it cannot be called outsides guilds
        DiscordPermissions permissions = channel.PermissionsFor(context.Guild!.CurrentMember);
        if (!permissions.HasPermission(DiscordPermission.SendMessages) || !permissions.HasPermission(DiscordPermission.ViewChannel))
        {
            await context.RespondAsync("Bot does not have permission to send messages in that channel");
            return;
        }
        else if (!permissions.HasPermission(DiscordPermission.ReadMessageHistory))
        {
            await context.RespondAsync("Bot does not have permission to read messages in that channel");
            return;
        }

        BotState state = context.ServiceProvider.GetRequiredService<BotState>();
        state.MessageLoggerChannelId = channel.Id;
        state.Save();

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

    [Command("resyncall")]
    [Description("Resyncs all members' roles with their Notion page")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    [RequirePermissions([], [DiscordPermission.Administrator])]
    public static async Task ResyncAllRoles(
        SlashCommandContext context,
        [Parameter("dryRun")]
        [Description("Set to false to actually affect roles")]
        bool dryRun = true)
    {
        await context.DeferResponseAsync();
        await context.ServiceProvider.GetRequiredService<RoleSyncer>().SyncAllMemberRoles(dryRun);
        await context.RespondAsync("Finished");
    }

    [Command("resyncmemberroles")]
    [Description("Resyncs a member's roles with their Notion page")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public static async Task ResyncMemberRoles(
        SlashCommandContext context,
        [Parameter("member")]
        [Description("The member to sync the roles of")]
        DiscordMember member,
        [Parameter("dryRun")]
        [Description("Set to false to actually affect roles")]
        bool dryRun = true)
    {
        await context.DeferResponseAsync();
        await context.ServiceProvider.GetRequiredService<RoleSyncer>().SyncRoles(member, dryRun);
        await context.RespondAsync("Finished");
    }

    [Command("checkoutall")]
    [Description("Checks out all members that are checked in")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public static async Task CheckoutAll(SlashCommandContext context)
    {
        context.ServiceProvider.GetRequiredService<BotState>().Members.Clear();
        await context.RespondAsync("Finished");
    }

    [Command("checkinout")]
    [Description("Checks you into/out of the shop and updates the member list")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    public static Task CheckInOut(SlashCommandContext context)
    {
        return CheckInOutInternal(context.Interaction, context.ServiceProvider.GetRequiredService<BotState>());
    }

    internal static async Task CheckInOutInternal(DiscordInteraction interaction, BotState state)
    {
        await interaction.DeferAsync(true);

        // Member is not since this command cannot be called outside of
        // guilds
        DiscordMember member = (interaction.User as DiscordMember)!;

            // Find index of member in list
            int memberIndex = -1;
            for (int i = 0; i < state.Members.Count; i++)
            {
                if (state.Members[i].member.Id == member.Id)
                {
                    memberIndex = i;
                    state.Members[i] = (member, state.Members[i].time);
                    break;
                }
            }

        // Update member list
        if (memberIndex == -1)
        {
            state.Members.Add((member, interaction.CreationTimestamp));
        }
        else
        {
            state.Members.RemoveAt(memberIndex);
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