using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notion.Client;

namespace TrickFireDiscordBot.Services.Notion;

public class RepeatingCalendarEvents(
    ILogger<RepeatingCalendarEvents> logger, 
    IOptions<RepeatingCalendarEventsOptions> options, 
    WebhookListener listener, 
    INotionClient notionClient)
    : NotionWebhookService<Page>(logger, listener), IAutoRegisteredService
{
    public override string WebhookEndpoint => "/repeating-event";

    protected override void OnPageWebhook(Page notionPage)
    {
        PageQueue.Writer.TryWrite(notionPage);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Page page = await PageQueue.Reader.ReadAsync(stoppingToken);
                
                // Skip if page already has an original event page
                RelationPropertyValue originalPage = (page.Properties[options.Value.OriginalEventPropertyName] as RelationPropertyValue)!;
                RepeatedMeeting meeting = new(options.Value, page);
                if (meeting.OriginalPageId != null || meeting.RepeatEvery == null 
                    || meeting.RepeatUntil == null || meeting.EventTimeStart == null
                    || meeting.EventTimeSpan == null)
                {
                    continue;
                }

                // Add given page to original event page
                originalPage.Relation.Add(new ObjectId() { Id = page.Id });
                await notionClient.Pages.UpdatePropertiesAsync(page.Id, page.Properties, stoppingToken);
                await Task.Delay(333, stoppingToken);

                // No cancellation token so all events are properly made
                await CreateRepeatedMeeting(new RepeatedMeeting(options.Value, page));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Repeating Calendar Events main loop exception:");
            }
        }
    }

    private async Task CreateRepeatedMeeting(RepeatedMeeting meeting)
    {
        // Create required pages
        DateTimeOffset eventTime = meeting.EventTimeStart!.Value + meeting.RepeatEvery!.Value;

        // Add one day so that the effective end date is 11:59pm
        DateTimeOffset repeatUntil = meeting.RepeatUntil!.Value.AddDays(1);
        while (eventTime < repeatUntil)
        {
            // Create a copy because if not weird stuff happens that I don't
            // fully understand
            Dictionary<string, PropertyValue> properties = new(meeting.Page.Properties);

            DatePropertyValue eventTimeProperty = (properties[options.Value.EventTimePropertyName] as DatePropertyValue)!;
            eventTimeProperty.Date.Start = eventTime;
            eventTimeProperty.Date.End = eventTime + meeting.EventTimeSpan!;
            await notionClient.Pages.CreateAsync(new PagesCreateParameters()
            {
                Properties = properties,
                Parent = new DatabaseParentInput() { DatabaseId = (meeting.Page.Parent as DatabaseParent)!.DatabaseId }
            });
            await Task.Delay(333);
            eventTime += meeting.RepeatEvery!.Value;
        }
    }

    /// <inheritdoc/>
    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddHostedService<RepeatingCalendarEvents>()
            .ConfigureTypeSection<RepeatingCalendarEventsOptions>(builder.Configuration);
    }

    private class RepeatedMeeting
    {
        public TimeSpan? RepeatEvery { get; }
        public DateTimeOffset? EventTimeStart { get; }
        public TimeSpan? EventTimeSpan { get; }
        public DateTimeOffset? RepeatUntil { get; }
        public string? OriginalPageId { get; }
        public Page Page { get; }

        public RepeatedMeeting(RepeatingCalendarEventsOptions options, Page page)
        {
            DatePropertyValue repeatEveryProperty = (page.Properties[options.RepeatEveryPropertyName] as DatePropertyValue)!;
            RepeatEvery = repeatEveryProperty.Date.End - repeatEveryProperty.Date.Start;

            DatePropertyValue eventTimeProperty = (page.Properties[options.EventTimePropertyName] as DatePropertyValue)!;
            EventTimeStart = eventTimeProperty.Date.Start;
            EventTimeSpan = eventTimeProperty.Date.End - EventTimeStart;
            RepeatUntil = (page.Properties[options.RepeatUntilPropertyName] as DatePropertyValue)!.Date.Start;

            OriginalPageId = (page.Properties[options.OriginalEventPropertyName] as RelationPropertyValue)!
                .Relation.FirstOrDefault()?.Id;

            Page = page;
        }
    }
}

public class RepeatingCalendarEventsOptions
{
    /// <summary>
    /// The name of the database property for a repeated meeting's repeat every
    /// </summary>
    public string RepeatEveryPropertyName { get; set; } = "";

    /// <summary>
    /// The name of the database property for a repeated meeting's repeat until
    /// </summary>
    public string RepeatUntilPropertyName { get; set; } = "";

    /// <summary>
    /// The name of the database property for a repeated meeting's original
    /// event
    /// </summary>
    public string OriginalEventPropertyName { get; set; } = "";


    /// <summary>
    /// The name of the database property for a repeated meeting's event time
    /// </summary>
    public string EventTimePropertyName { get; set; } = "";
}
