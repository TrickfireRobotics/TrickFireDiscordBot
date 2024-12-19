using DSharpPlus.Entities;
using System.Collections.ObjectModel;

namespace TrickFireDiscordBot
{
    /// <summary>
    /// A class that represents the state of the bot.
    /// </summary>
    public class BotState
    {
        /// <summary>
        /// The list of members checked in.
        /// </summary>
        public ObservableCollection<(DiscordMember member, DateTimeOffset time)> Members { get; } = [];
    }
}