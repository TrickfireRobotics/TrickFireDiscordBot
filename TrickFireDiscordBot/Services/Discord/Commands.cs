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
        (DiscordMessage? message, string? error) = await CheckChannelAndMessagePerms(
            channel, messageId, context.Guild!
        );
        if (error is not null)
        {
            await context.RespondAsync(error);
            return;
        }

        // Delete old message
        BotState state = context.ServiceProvider.GetRequiredService<BotState>();
        (state.CheckInChannelId, state.ListMessageId) = await DeleteOldChannelMessageAndUpdate(
            channel, state.CheckInChannelId, message, state.ListMessageId, context.Guild!
        );
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
        string? res = CheckChannelPerms(channel, context.Guild!);
        if (res is not null)
        {
            await context.RespondAsync(res);
            return;
        }

        BotState state = context.ServiceProvider.GetRequiredService<BotState>();
        state.MessageLoggerChannelId = channel.Id;
        state.Save();

        // Return success
        await context.RespondAsync("Channel succesfully set!");
    }

    [Command("setfeedbackchannels")]
    [Description("Sets the channels the bot sends feedback messages and embed to")]
    [InteractionAllowedContexts(DiscordInteractionContextType.Guild)]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public static async Task SetFeedbackChannels(
        SlashCommandContext context,
        [Parameter("feedbackchannel")]
        [Description("The channel to feedback to")]
        DiscordChannel feedbackChannel,
        [Parameter("formchannel")]
        [Description("The channel to send the feedback form to")]
        DiscordChannel formChannel,
        [Parameter("formmessageid")]
        [Description("The message id of the form message")]
        ulong? formMessageId = null)
    {
        // Check permissions and existence of message
        (DiscordMessage? message, string? res) = await CheckChannelAndMessagePerms(
            formChannel, formMessageId, context.Guild!
        );
        res ??= CheckChannelPerms(feedbackChannel, context.Guild!);
        if (res is not null)
        {
            await context.RespondAsync(res);
            return;
        }

        // Delete old message
        BotState state = context.ServiceProvider.GetRequiredService<BotState>();
        (state.FeedbackFormChannelId, state.FeedbackFormMessageId) = await DeleteOldChannelMessageAndUpdate(
            formChannel, state.FeedbackFormChannelId, message, state.FeedbackFormMessageId, context.Guild!
        );

        // Send new channel if old one was deleted
        if (state.FeedbackFormMessageId == 0)
        {
            try
            {
                DiscordMessage newMessage = await formChannel.SendMessageAsync(
                    context.ServiceProvider.GetRequiredService<FeedbackService>().formMessage
                );
                state.FeedbackFormMessageId = newMessage.Id;
            }
            catch (DiscordException)
            {
                await context.RespondAsync("Failed to send new feedback form message. Old one was still deleted");
                return;
            }
        }

        state.FeedbackChannelId = feedbackChannel.Id;
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
        return context.ServiceProvider.GetRequiredService<CheckInOutService>().CheckInOutInternal(context.Interaction);
    }

    /// <summary>
    /// Checks the given channel for the read message history and send message
    /// permissions and checks if the given message id exists, is sent by the
    /// bot, and was sent in the same channel as given.
    /// </summary>
    /// <param name="channel">The channel to check</param>
    /// <param name="messageId">The message id to check or null</param>
    /// <param name="guild">The guild to check in</param>
    /// <returns>A tuple of the found message and an error</returns>
    private static async Task<(DiscordMessage? message, string? error)> CheckChannelAndMessagePerms(
        DiscordChannel channel, 
        ulong? messageId, 
        DiscordGuild guild
    ) {
        string? res = CheckChannelPerms(channel, guild);
        if (res is not null)
        {
            return (null, res);
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
                return (null, "Improper permissions to get message");
            }

            if (message is null)
            {
                return (null, "Message not found");
            }

            // Fail if bot isn't message author or message channel doesn't match
            // passed in channel
            if (message.Author?.Id != guild.CurrentMember.Id)
            {
                return (message, "Message must have been sent by bot");
            }
            else if (message.ChannelId != channel.Id)
            {
                return (message, "Message must be sent in same channel as inputted");
            }
        }

        return (message, null);
    }

    /// <summary>
    /// Checks the given channel for the read message history and send message
    /// permissions.
    /// </summary>
    /// <param name="channel">The channel to check</param>
    /// <param name="guild">The guild to check in</param>
    /// <returns>An error message or null</returns>
    private static string? CheckChannelPerms(DiscordChannel channel, DiscordGuild guild)
    {
        // Guild is not null because it cannot be called outsides guilds
        DiscordPermissions permissions = channel.PermissionsFor(guild.CurrentMember);
        if (!permissions.HasPermission(DiscordPermission.SendMessages) || !permissions.HasPermission(DiscordPermission.ViewChannel))
        {
            return "Bot does not have permission to send messages in that channel";
        }
        else if (!permissions.HasPermission(DiscordPermission.ReadMessageHistory))
        {
            return "Bot does not have permission to read messages in that channel";
        }
        return null;
    }

    
    /// <summary>
    /// Deletes the current message if it is out of date, then does some checks
    /// to figure out the updated channel and message ids.
    /// </summary>
    /// <param name="channel">The channel updating to</param>
    /// <param name="currChannelId">The current channel id in state</param>
    /// <param name="message">The message updating to</param>
    /// <param name="currMessageId">The current message id in state</param>
    /// <param name="guild">The guild to check in</param>
    /// <returns>The new channel and message ids</returns>
    private static async Task<(ulong newChannelId, ulong newMessageId)> DeleteOldChannelMessageAndUpdate(
        DiscordChannel channel,
        ulong currChannelId,
        DiscordMessage? message,
        ulong currMessageId,
        DiscordGuild guild
    ) {

        // Delete old message
        try
        {
            if (message is null || message.Id != currMessageId)
            {
                DiscordChannel oldChannel = await guild.GetChannelAsync(currChannelId);
                DiscordMessage oldMessage = await oldChannel.GetMessageAsync(currMessageId);
                await oldMessage.DeleteAsync();
            }
        }
        catch (NotFoundException) { }
        catch (UnauthorizedException) { }

        // Update settings in config
        ulong retChannelId = currChannelId != channel.Id ? channel.Id : currChannelId;
        if (message is not null && currMessageId != message.Id)
        {
            return (retChannelId, message.Id);
        }
        else if (message is null)
        {
            return (retChannelId, 0);
        }

        return (retChannelId, currMessageId);
    }
}