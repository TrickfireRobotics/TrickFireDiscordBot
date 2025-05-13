using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TrickFireDiscordBot.Services.Discord;

public class FeedbackService(IOptions<FeedbackServiceOptions> options, BotState botState, DiscordService discord) 
    : IHostedService, IAutoRegisteredService, IEventHandler<ComponentInteractionCreatedEventArgs>, IEventHandler<ModalSubmittedEventArgs>
{
    private const string ModalInteractionId = "FeedbackForm";
    private const string InputInteractionId = "FeedbackFormInput";
    private const string OpenModalInteractionId = "FeedbackFormModal";

    public readonly DiscordMessageBuilder formMessage = new DiscordMessageBuilder()
        .EnableV2Components()
        .AddContainerComponent(new DiscordContainerComponent([
            new DiscordTextDisplayComponent(options.Value.FeedbackFormMessage),
            new DiscordActionRowComponent([ new DiscordButtonComponent(
                DiscordButtonStyle.Success,
                OpenModalInteractionId,
                "Submit Feedback"
            ) ])
        ], color: new DiscordColor("19a24a")));

    private static readonly DiscordInteractionResponseBuilder modal = new DiscordInteractionResponseBuilder()
        .EnableV2Components()
        .WithTitle("Submit Feedback")
        .WithCustomId(ModalInteractionId)
        .AddActionRowComponent(new DiscordActionRowComponent([ new DiscordTextInputComponent(
            "Feedback",
            InputInteractionId,
            style: DiscordTextInputStyle.Paragraph,
            placeholder: "Please include as much context as possible"
        ) ]));

    private static readonly DiscordFollowupMessageBuilder modalConfirm = new DiscordFollowupMessageBuilder()
        .WithContent("Feedback sent. Thanks!");

    private DiscordChannel? feedbackChannel = null;

    public Task HandleEventAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
    {
        if (e.Id != OpenModalInteractionId)
        {
            return Task.CompletedTask;
        }
        return e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.Modal, modal);
    }

    public async Task HandleEventAsync(DiscordClient sender, ModalSubmittedEventArgs e)
    {
        if (e.Id != ModalInteractionId)
        {
            return;
        }

        await e.Interaction.DeferAsync(true);
        feedbackChannel ??= await discord.MainGuild.GetChannelAsync(botState.FeedbackChannelId);
        await feedbackChannel.SendMessageAsync(new DiscordMessageBuilder()
            .EnableV2Components()
            .AddContainerComponent(new DiscordContainerComponent([
                new DiscordTextDisplayComponent("# Feedback Received"),
                new DiscordSeparatorComponent(divider: true),
                new DiscordTextDisplayComponent(e.Values[InputInteractionId])
            ]))
        );
        await e.Interaction.CreateFollowupMessageAsync(modalConfirm);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (botState.FeedbackFormChannelId == 0 || botState.FeedbackFormMessageId == 0)
        {
            return;
        }

        try
        {
            DiscordChannel channel = await discord.MainGuild.GetChannelAsync(botState.FeedbackFormChannelId);
            DiscordMessage message = await channel.GetMessageAsync(botState.FeedbackFormMessageId);
            await message.ModifyAsync(formMessage);
            await Task.Delay(3000, cancellationToken);
        }
        catch (DiscordException)
        {
            discord.Client.Logger.LogError("Failed to update form message on startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddHostedService<FeedbackService>()
            .ConfigureTypeSection<FeedbackServiceOptions>(builder.Configuration);
    }
}

public class FeedbackServiceOptions
{
    /// <summary>
    /// The text content of the feedback form
    /// </summary>
    public string FeedbackFormMessage { get; set; } = "";
}