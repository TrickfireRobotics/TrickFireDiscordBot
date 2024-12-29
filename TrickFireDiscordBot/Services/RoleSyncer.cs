using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Notion.Client;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TrickFireDiscordBot.Services.Discord;
using TrickFireDiscordBot.Services.Notion;

namespace TrickFireDiscordBot.Services;

public class RoleSyncer(
    ILogger<RoleSyncer> logger,
    DiscordMessageLogger messageLogger,
    INotionClient notionClient,
    WebhookListener listener,
    DiscordService discordService,
    IOptions<RoleSyncerOptions> options)
    : BackgroundService, IAutoRegisteredService
{
    public const string WebhookEndpoint = "/members";

    private readonly Regex _technicalLeadRegex = new(options.Value.TechnicalLeadRegex);

    private static readonly Channel<Page> _pageQueue = Channel.CreateUnbounded<Page>();

    private readonly Dictionary<string, DiscordRole> _discordRoleCache = [];
    private string? _teamPageNamePropertyId = null;


    public override Task StartAsync(CancellationToken cancellationToken)
    {
        listener.OnWebhookReceived += OnWebhook;

        foreach (DiscordRole role in discordService.MainGuild.Roles.Values)
        {
            _discordRoleCache.Add(role.Name, role);
        }

        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        listener.OnWebhookReceived -= OnWebhook;
    }

    public async Task SyncAllMemberRoles(bool dryRun = true)
    {
        Task<PaginatedList<Page>> nextPageQuery(string? cursor) =>
            notionClient.Databases.QueryAsync(
                options.Value.MembersDatabaseId,
                new DatabasesQueryParameters()
                {
                    StartCursor = cursor
                }
            );

        HashSet<DiscordMember?> processedMembers = [];
        await foreach (Page page in PaginatedListHelper.GetEnumerable(nextPageQuery))
        {
            await Task.Delay(333);
            DiscordMember? member = await SyncRoles(page, dryRun);
            processedMembers.Add(member);
            logger.LogInformation("\n");
        }

        logger.LogInformation("\nNo notion page users:");
        DiscordRole inactiveRole = _discordRoleCache.Values.First(role => role.Id == options.Value.InactiveRoleId);
        await foreach (DiscordMember member in discordService.MainGuild.GetAllMembersAsync())
        {
            if (processedMembers.Contains(member))
            {
                continue;
            }
            if (!dryRun && !member.Roles.Contains(inactiveRole))
            {
                await member.GrantRoleAsync(inactiveRole);
                await Task.Delay(333);
            }
            logger.LogInformation("{}, ({})", member.DisplayName, member.Username);
        }
    }

    private void OnWebhook(HttpListenerRequest request)
    {
        if (request.RawUrl == null || !request.RawUrl.StartsWith(WebhookEndpoint))
        {
            return;
        }

        using StreamReader reader = new(request.InputStream);
        string json = reader.ReadToEnd();
        Automation? automation = JsonConvert.DeserializeObject<Automation>(json);
        if (automation == null || automation.Data is not Page page)
        {
            logger.LogWarning("Could not parse automation: {}", json);
            return;
        }

        _pageQueue.Writer.TryWrite(page);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncRoles(await _pageQueue.Reader.ReadAsync(stoppingToken, true));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in RoleSyncer main loop: ");
            }
        }
    }

    private async Task<DiscordMember?> SyncRoles(Page notionPage, bool dryRun = true)
    {
        Console.WriteLine(JsonConvert.SerializeObject(notionPage, Formatting.Indented));

        DiscordMember? member = await GetMember(notionPage);
        if (member is null)
        {
            return null;
        }

        IEnumerable<DiscordRole> newRoles = await GetRoles(notionPage);
        logger.LogInformation(member.DisplayName);
        foreach (DiscordRole role in newRoles)
        {
            logger.LogInformation(role!.Name);
        }

        if (!dryRun)
        {
            int highestRole = discordService.MainGuild.CurrentMember.Roles.Max(role => role.Position);
            if (member.Roles.Any(role => options.Value.SafeRoleIds.Contains(role.Id)))
            {
                return member;
            }

            await member.ModifyAsync(model =>
            {
                List<DiscordRole> rolesWithLeadership = new(newRoles);
                foreach (DiscordRole role in member.Roles.Where(role => role.Position >= highestRole))
                {
                    rolesWithLeadership.Add(role);
                }
                model.Roles = rolesWithLeadership;
            });
            await Task.Delay(1000);
        }

        return member;
    }

    private async Task<DiscordMember?> GetMember(Page notionPage)
    {
        // We want this to fail hard if something is wrong
        string? username = (notionPage.Properties[options.Value.DiscordUsernamePropertyName]
            as FormulaPropertyValue)!.Formula.String;

        if (string.IsNullOrWhiteSpace(username))
        {
            logger.LogWarning("User with page url {} has no discord.", notionPage.Url);
            messageLogger.LogWarning("User with page url {} has no discord.", notionPage.Url);
            return null;
        }

        username = username.TrimStart('@').ToLower();

        DiscordMember? member = discordService.MainGuild.Members.Values
            .FirstOrDefault(member => member.Username == username);

        if (member is null)
        {
            IReadOnlyList<DiscordMember> search = await discordService.MainGuild.SearchMembersAsync(username);
            if (search.Count == 0 || search[0].Username != username)
            {
                logger.LogWarning("Could not find member: {}", username);
                messageLogger.LogWarning("Could not find member: {}", username);
            }
            else
            {
                member = search[0];
            }
            await Task.Delay(333);
        }
        return member;
    }

    private async Task<IEnumerable<DiscordRole>> GetRoles(Page notionPage)
    {
        List<DiscordRole> roles = [];

        DiscordRole? activeRole = GetActiveRole(notionPage);
        if (activeRole is not null)
        {
            roles.Add(activeRole);
        }

        roles.AddRange(await GetTeams(notionPage));

        MultiSelectPropertyValue positions = (notionPage.Properties[options.Value.ClubPositionsPropertyName]
            as MultiSelectPropertyValue)!;
        foreach (SelectOption option in positions.MultiSelect)
        {
            if (option.Name == "Individual Contributor")
            {
                continue;
            }

            if (_technicalLeadRegex.IsMatch(option.Name))
            {
                roles.Add(_discordRoleCache.Values.First(role => role.Id == options.Value.TechnicalLeadRoleId));
            }

            // Remove team suffix to make roles a little easier to read
            DiscordRole? positionRole = GetRoleOrDefault(option.Name.Replace(" Team", ""));
            if (positionRole is not null)
            {
                roles.Add(positionRole);
            }
        }

        MultiSelectPropertyValue disciplines = (notionPage.Properties[options.Value.DisciplinesPropertyName]
            as MultiSelectPropertyValue)!;
        foreach (SelectOption option in disciplines.MultiSelect)
        {
            DiscordRole? positionRole = GetRoleOrDefault(option.Name);
            if (positionRole is not null)
            {
                roles.Add(positionRole);
            }
        }
        return roles;
    }

    private DiscordRole? GetActiveRole(Page notionPage)
    {
        string activeValue = (notionPage.Properties[options.Value.ActivePropertyName]
            as SelectPropertyValue)!.Select.Name;
        if (activeValue == "Active")
        {
            return null;
        }
        else
        {
            return GetRoleOrDefault(activeValue);
        }
    }

    private async Task<IEnumerable<DiscordRole>> GetTeams(Page notionPage)
    {
        List<DiscordRole> roles = [];
        List<ObjectId> teamIds = (notionPage.Properties[options.Value.TeamsPropertyName]
            as RelationPropertyValue)!.Relation;
        foreach (ObjectId id in teamIds)
        {
            string teamName = await GetTeamName(id);

            // Remove team suffix to make roles a little easier to read
            DiscordRole? role = GetRoleOrDefault(teamName.Replace(" Team", ""));
            if (role is not null)
            {
                roles.Add(role);
            }

        }

        return roles;
    }

    private async Task<string> GetTeamName(ObjectId id)
    {
        // Try using our cached property id if it exists
        if (_teamPageNamePropertyId is not null)
        {
            try
            {
                ListPropertyItem item = (await notionClient.Pages.RetrievePagePropertyItemAsync(new RetrievePropertyItemParameters()
                {
                    PageId = id.Id,
                    PropertyId = _teamPageNamePropertyId
                }) as ListPropertyItem)!;
                return (item.Results.First() as TitlePropertyItem)!.Title.PlainText;
            }
            catch (NotionApiException ex)
            {
                // If not found, then move on to rest of method
                // If another error, rethrow the error to move it up the
                // stack
                if (ex.NotionAPIErrorCode != NotionAPIErrorCode.ObjectNotFound)
                {
                    throw;
                }
            }
        }

        // If it doesn't or fails, then recache it and get the whole page
        Page teamPage = await notionClient.Pages.RetrieveAsync(id.Id);
        TitlePropertyValue nameValue = (teamPage.Properties[options.Value.TeamNamePropertyName]
            as TitlePropertyValue)!;
        _teamPageNamePropertyId = nameValue.Id;
        return nameValue.Title[0].PlainText;
    }

    private DiscordRole? GetRoleOrDefault(string roleName)
    {
        if (!_discordRoleCache.TryGetValue(roleName, out DiscordRole? role))
        {
            logger.LogWarning("Could not find role with name: {}", roleName);
            messageLogger.LogWarning("Could not find role with name: {}", roleName);
        }

        return role;
    }

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddInjectableHostedService<RoleSyncer>()
            .ConfigureTypeSection<RoleSyncerOptions>(builder.Configuration);
    }
}

public class RoleSyncerOptions()
{
    /// <summary>
    /// The id of the Teams page database in Notion.
    /// </summary>
    public string TeamsDatabaseId { get; set; } = "";

    /// <summary>
    /// The name of the database property for a team's name
    /// </summary>
    public string TeamNamePropertyName { get; set; } = "";

    /// <summary>
    /// The id of the Members page database in Notion.
    /// </summary>
    public string MembersDatabaseId { get; set; } = "";

    /// <summary>
    /// The name of the database property for a member's discord username.
    /// </summary>
    public string DiscordUsernamePropertyName { get; set; } = "";

    /// <summary>
    /// The name of the database property for a member's active status.
    /// </summary>
    public string ActivePropertyName { get; set; } = "";

    /// <summary>
    /// The name of the database property for a member's club positions.
    /// </summary>
    public string ClubPositionsPropertyName { get; set; } = "";

    /// <summary>
    /// The name of the database property for a member's disciplines.
    /// </summary>
    public string DisciplinesPropertyName { get; set; } = "";

    /// <summary>
    /// The name of the database property for a member's teams.
    /// </summary>
    public string TeamsPropertyName { get; set; } = "";

    /// <summary>
    /// The id of the technical lead role.
    /// </summary>
    public ulong TechnicalLeadRoleId { get; set; } = 0;

    /// <summary>
    /// A regex determining if the club position listed in notion makes a
    /// member a technical lead.
    /// </summary>
    public string TechnicalLeadRegex { get; set; } = "";

    /// <summary>
    /// The id of the inactive role.
    /// </summary>
    public ulong InactiveRoleId { get; set; } = 0;

    /// <summary>
    /// The ids of roles that make their members ignored by the syncer.
    /// </summary>
    public HashSet<ulong> SafeRoleIds { get; set; } = [];
}
