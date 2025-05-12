using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace TrickFireDiscordBot.Services;

/// <summary>
/// A class that represents the state of the bot.
/// </summary>
public class BotState : IAutoRegisteredService
{
    /// <summary>
    /// The list of members checked in.
    /// </summary>
    public ObservableCollection<(DiscordMember member, DateTimeOffset time)> Members { get; } = [];

    /// <summary>
    /// The id of the message that has the list of members in the shop.
    /// </summary>
    public ulong ListMessageId { get; set; } = 0;

    /// <summary>
    /// The id of the channel that current attendance is sent to.
    /// </summary>
    public ulong CheckInChannelId { get; set; } = 0;

    /// <summary>
    /// The id of the channel to log debug messages into.
    /// </summary>
    public ulong MessageLoggerChannelId { get; set; } = 0;


    /// <summary>
    /// The id of the channel to send feedback to.
    /// </summary>
    public ulong FeedbackChannelId { get; set; } = 0;

    /// <summary>
    /// The id of the channel to send the feedback form to.
    /// </summary>
    public ulong FeedbackFormChannelId { get; set; } = 0;

    /// <summary>
    /// The id of the feedback form message.
    /// </summary>
    public ulong FeedbackFormMessageId { get; set; } = 0;

    /// <summary>
    /// The id of the club info message.
    /// </summary>
    public ulong ClubInfoMessageId { get; set; } = 0;

    private BotStateOptions Options { get; }

        public BotState(IOptions<BotStateOptions> options)
        {
            Options = options.Value;
            if (!File.Exists(Options.FileLocation) || string.IsNullOrWhiteSpace(File.ReadAllText(Options.FileLocation)))
            {
                File.WriteAllText(Options.FileLocation, "{}");
            }
            JsonConvert.PopulateObject(File.ReadAllText(Options.FileLocation), this);

            Members.CollectionChanged += (_, __) => Save();
        }

    public void Save()
    {
        lock (this)
        {
            File.WriteAllText(Options.FileLocation, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddSingleton<BotState>()
            .ConfigureTypeSection<BotStateOptions>(builder.Configuration); ;
    }
}

public class BotStateOptions
{
    public string FileLocation { get; set; } = "";
}