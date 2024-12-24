using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notion.Client;
using System.Collections.Specialized;
using System.Threading.Channels;
using TrickFireDiscordBot.Services.Notion;

namespace TrickFireDiscordBot.Services;

public class AttendanceTracker(ILogger<AttendanceTracker> logger, INotionClient notionClient, BotState botState, IOptions<AttendanceTrackerOptions> options)
    : BackgroundService, IAutoRegisteredService
{
    private readonly Channel<NotifyCollectionChangedEventArgs> channel
        = Channel.CreateUnbounded<NotifyCollectionChangedEventArgs>();

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        botState.Members.CollectionChanged += OnMembersChange;

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        botState.Members.CollectionChanged -= OnMembersChange;
    }

    private void OnMembersChange(object? _, NotifyCollectionChangedEventArgs ev)
    {
        channel.Writer.TryWrite(ev);
    }

    /// <inheritdoc/>
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
                        await Task.Delay(333, stoppingToken);
                        await MemberCheckedIn(newMember, newTime);
                        break;
                    case NotifyCollectionChangedAction.Reset:
                        await Reset();
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

        // Update last page with checkout date
        await Task.Delay(333);
        Page? lastCheckin = await GetLastCheckin(memberPage);
        if (lastCheckin != null)
        {
            await CheckoutPage(lastCheckin, time.UtcDateTime);
        }

        // Create page for most recent checkin
        await Task.Delay(333);
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
            }
        });
    }

    private async Task MemberCheckedOut(DiscordMember member, DateTimeOffset time)
    {
        Page? memberPage = await GetMemberNotion(member);
        if (memberPage == null)
        {
            return;
        }

        // Update last page with checkout date
        await Task.Delay(333);
        Page? lastCheckin = await GetLastCheckin(memberPage);
        if (lastCheckin != null)
        {
            await CheckoutPage(lastCheckin, time.UtcDateTime, false);
        }
    }

    private async Task Reset()
    {
        Task<PaginatedList<Page>> query(string? cursor)
        {
            return notionClient.Databases.QueryAsync(
                options.Value.MemberAttendanceDatabaseId,
                new DatabasesQueryParameters()
                {
                    Filter = new DateFilter("Checkout Time", isEmpty: true),
                    StartCursor = cursor
                }
            );
        }

        await foreach (Page page in PaginatedListHelper.GetEnumerable(query))
        {
            await CheckoutPage(page);
        }
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

    private async Task CheckoutPage(Page page, DateTime? checkoutTime = null, bool checkedOutByBot = true)
    {
        DatePropertyValue checkoutProperty = (page.Properties["Checkout Time"] as DatePropertyValue)!;
        CheckboxPropertyValue checkedOutByBotProperty = (page.Properties["Checked out by Bot?"] as CheckboxPropertyValue)!;
        // Only update if checkout time is empty, and has not been checked
        // out by bot
        if (checkoutProperty.Date == null && checkedOutByBotProperty.Checkbox != true)
        {
            // Update page
            checkedOutByBotProperty.Checkbox = checkedOutByBot;
            checkoutProperty.Date = checkoutTime == null ? null : new Date()
            {
                Start = checkoutTime,
                TimeZone = "GMT"
            };
            await Task.Delay(333);
            await notionClient.Pages.UpdatePropertiesAsync(
                page.Id,
                new Dictionary<string, PropertyValue>
                {
                    { "Checkout Time",  checkoutProperty },
                    { "Checked out by Bot?", checkedOutByBotProperty }
                }
            );
        }
    }

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
