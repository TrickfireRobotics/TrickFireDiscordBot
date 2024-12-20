using Notion.Client;
using System.Collections.Specialized;

namespace TrickFireDiscordBot.Services
{
    public class AttendanceTracker
    {
        public NotionClient NotionClient { get; }
        public BotState BotState { get; }

        public AttendanceTracker(NotionClient notionClient, BotState botState)
        {
            NotionClient = notionClient;
            BotState = botState;

            BotState.Members.CollectionChanged += OnMembersChange;
        }

        private void OnMembersChange(object? _, NotifyCollectionChangedEventArgs ev)
        {
            OnMembersChangeAsync(ev).GetAwaiter().GetResult();
        }

        private async Task OnMembersChangeAsync(NotifyCollectionChangedEventArgs ev)
        {

        }
    }

    public class AttendanceTrackerOptions
    {

        /// <summary>
        /// The id of the Members Attendance page database in Notion.
        /// </summary>
        public string MemberAttendanceDatabaseId { get; set; } = "";
    }
}
