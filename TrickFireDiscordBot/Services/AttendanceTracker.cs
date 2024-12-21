using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notion.Client;
using System.Collections.Specialized;
using System.Threading.Channels;

namespace TrickFireDiscordBot.Services;

public class AttendanceTracker(ILogger<AttendanceTracker> logger, INotionClient notionClient, BotState botState, IOptions<AttendanceTrackerOptions> options)
    : BackgroundService, IAutoRegisteredService
{
    private readonly Channel<NotifyCollectionChangedEventArgs> channel
        = Channel.CreateUnbounded<NotifyCollectionChangedEventArgs>();

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        botState.Members.CollectionChanged += OnMembersChange;

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        botState.Members.CollectionChanged -= OnMembersChange;
    }

    private void OnMembersChange(object? _, NotifyCollectionChangedEventArgs ev)
    {
        channel.Writer.TryWrite(ev);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                NotifyCollectionChangedEventArgs ev = await channel.Reader.ReadAsync(stoppingToken);
                DiscordMember member;
                DateTimeOffset time;
                switch (ev.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        (member, time) = ((DiscordMember, DateTimeOffset))ev.NewItems![0]!;
                        await MemberCheckedIn(member, time);
                        break;
                    case NotifyCollectionChangedAction.Remove:
                        (member, time) = ((DiscordMember, DateTimeOffset))ev.OldItems![0]!;
                        await MemberCheckedOut(member, time);
                        break;
                    case NotifyCollectionChangedAction.Replace:
                        (member, time) = ((DiscordMember, DateTimeOffset))ev.OldItems![0]!;
                        (DiscordMember newMember, DateTimeOffset newTime) = ((DiscordMember, DateTimeOffset))ev.OldItems![0]!;

                        if (member.Id == newMember.Id)
                        {
                            break;
                        }

                        await MemberCheckedOut(member, time);
                        await MemberCheckedIn(newMember, newTime);
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        await Reset(DateTimeOffset.Now);
                        break;
                    case NotifyCollectionChangedAction.Move:
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in AttendanceTracker loop: ");
            }
        }
    }

    private async Task MemberCheckedIn(DiscordMember member, DateTimeOffset time)
    {
        Page? memberPage = await GetMemberNotion(member);
        if (memberPage == null)
        {
            return;
        }

        Page? lastCheckin = await GetLastCheckin(memberPage);
        int checkinCount = 1;
        if (lastCheckin != null 
            && (lastCheckin.Properties["Checked in this Week?"] as FormulaPropertyValue)!.Formula.Boolean == true)
        {
            checkinCount += (int)((lastCheckin.Properties["Checkin Number"] as NumberPropertyValue)!.Number ?? 0);
        }


        await notionClient.Pages.CreateAsync(new PagesCreateParameters()
        {
            Parent = new DatabaseParentInput()
            {
                DatabaseId = options.Value.MemberAttendanceDatabaseId
            },
            Properties = new Dictionary<string, PropertyValue>()
            {
                { "Member Name", new TitlePropertyValue() { Title = [new RichTextText() { Text = new Text() { Content = member.DisplayName } }] } },
                { "Member", new RelationPropertyValue() { Relation = [new ObjectId() { Id = memberPage.Id }] } },
                { "Checkin Time", new DatePropertyValue() { Date = new Date() { Start = time.UtcDateTime, TimeZone = "GMT" } } },
                { "Checkin Number", new NumberPropertyValue() { Number = checkinCount } },
            }
        });
    }

    private async Task MemberCheckedOut(DiscordMember member, DateTimeOffset time)
    {

    }

    private async Task Reset(DateTimeOffset time)
    {

    }

    private async Task<Page?> GetMemberNotion(DiscordMember member)
    {
        List<Page> searchResults = (await notionClient.Databases.QueryAsync(
            options.Value.MembersDatabaseId,
            new DatabasesQueryParameters()
            {
                Filter = new FormulaFilter(
                    options.Value.DiscordUsernamePropertyName,
                    @string: new TextFilter.Condition(equal: member.Username)
                ),
                PageSize = 1
            }
        )).Results;
        if (searchResults.Count < 1)
        {
            logger.LogWarning("Discord member {}, ({})'s page could not be found", member.DisplayName, member.Username);
            return null;
        }

        return searchResults[0];
    }

    private async Task<Page?> GetLastCheckin(Page page)
        => (await notionClient.Databases.QueryAsync(
            options.Value.MemberAttendanceDatabaseId,
            new DatabasesQueryParameters()
            {
                Filter = new RelationFilter(
                    "Member",
                    contains: page.Id
                ),
                PageSize = 1,
                Sorts = [new Sort() { Property = "Checkin Time", Direction = Direction.Descending }]
            }
        )).Results.FirstOrDefault();

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddHostedService<AttendanceTracker>()
            .ConfigureTypeSection<AttendanceTrackerOptions>(builder.Configuration);
    }
}

public class AttendanceTrackerOptions
{

    /// <summary>
    /// The id of the Members Attendance page database in Notion.
    /// </summary>
    public string MemberAttendanceDatabaseId { get; set; } = "";

    /// <summary>
    /// The id of the Members page database in Notion.
    /// </summary>
    public string MembersDatabaseId { get; set; } = "";

    /// <summary>
    /// The name of the database property for a members' discord username.
    /// </summary>
    public string DiscordUsernamePropertyName { get; set; } = "";
}
