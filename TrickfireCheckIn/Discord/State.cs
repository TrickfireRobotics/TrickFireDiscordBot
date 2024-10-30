using DSharpPlus.Entities;
using System.Collections.ObjectModel;

namespace TrickfireCheckIn.Discord
{
    /// <summary>
    /// A class that represents the state of the bot.
    /// </summary>
    public static class State
    {
        /// <summary>
        /// The list of members checked in.
        /// </summary>
        public static ObservableCollection<(DiscordMember member, DateTimeOffset time)> Members { get; } = [];
    }
}