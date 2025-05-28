//using DSharpPlus.Entities;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Options;
//using Notion.Client;
//using System.Text.RegularExpressions;
//using TrickFireDiscordBot.Services.Discord;
//using TrickFireDiscordBot.Services.Notion;

//namespace TrickFireDiscordBot.Services;

//public class TaskSyncer(
//    ILogger<RoleSyncer> logger,
//    DiscordMessageLogger messageLogger,
//    INotionClient notionClient,
//    WebhookListener listener,
//    DiscordService discordService,
//    IOptions<RoleSyncerOptions> options)
//    : NotionWebhookService<TaskPage>(logger, listener), IAutoRegisteredService
//{
//    public override string WebhookEndpoint => "/members";

//    private readonly Regex _technicalLeadRegex = new(options.Value.TechnicalLeadRegex);

//    private readonly Dictionary<string, DiscordRole> _discordRoleCache = [];
//    private string? _teamPageNamePropertyId = null;

//    /// <inheritdoc/>
//    public override Task StartAsync(CancellationToken cancellationToken)
//    {
//        foreach (DiscordRole role in discordService.MainGuild.Roles.Values)
//        {
//            _discordRoleCache.Add(role.Name, role);
//        }

//        return base.StartAsync(cancellationToken);
//    }

//    protected override void OnPageWebhook(Page notionPage)
//    {
//        PageQueue.Writer.TryWrite(new MemberPage(options.Value, notionPage));
//    }

//    /// <summary>
//    /// Syncs the role of all members in the main guild.
//    /// </summary>
//    /// <param name="dryRun">Whether to actually have an effect</param>
//    public async Task SyncAllMemberRoles(bool dryRun = true)
//    {
//        async Task<PaginatedList<IWikiDatabase>> nextPageQuery(string? cursor) =>
//            await notionClient.Databases.QueryAsync(
//                options.Value.MembersDatabaseId,
//                new DatabasesQueryParameters()
//                {
//                    StartCursor = cursor
//                }
//            );

//        // Get all notion users and sync their roles
//        HashSet<DiscordMember?> processedMembers = [];
//        await foreach (IWikiDatabase wikiDB in PaginatedListHelper.GetEnumerable(nextPageQuery))
//        {
//            if (wikiDB is not Page page)
//            {
//                continue;
//            }
//            await Task.Delay(333);
//            DiscordMember? member = await SyncRoles(page: new MemberPage(options.Value, page), dryRun: dryRun);
//            processedMembers.Add(member);
//        }

//        // Get all discord users
//        DiscordRole inactiveRole = _discordRoleCache.Values.First(role => role.Id == options.Value.InactiveRoleId);
//        await foreach (DiscordMember member in discordService.MainGuild.GetAllMembersAsync())
//        {
//            // Filter members already processed
//            if (processedMembers.Contains(member))
//            {
//                continue;
//            }
//            await SyncRoles(member: member, page: null, dryRun: dryRun);
//        }
//    }

//    /// <inheritdoc/>
//    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//    {
//        while (!stoppingToken.IsCancellationRequested)
//        {
//            try
//            {
//                await SyncRoles(page: await PageQueue.Reader.ReadAsync(stoppingToken), dryRun: false);
//            }
//            catch (Exception ex)
//            {
//                logger.LogError(ex, "Error in RoleSyncer main loop: ");
//            }
//        }
//    }

//    /// <summary>
//    /// Syncs the roles of the given member with their corresponding Notion page
//    /// if it exists.
//    /// </summary>
//    /// <param name="member">The member to sync the roles of</param>
//    /// <param name="dryRun">Whether to actually affect things or not</param>
//    public async Task SyncRoles(DiscordMember member, bool dryRun = true)
//    {
//        PaginatedList<IWikiDatabase> search = await notionClient.Databases.QueryAsync(
//            options.Value.MembersDatabaseId,
//            new DatabasesQueryParameters()
//            {
//                Filter = new FormulaFilter(options.Value.DiscordUsernamePropertyName, @string: new TextFilter.Condition(equal: member.Username))
//            }
//        );

//        MemberPage? page = null;
//        if (search.Results.Count == 1)
//        {
//            page = new MemberPage(options.Value, (Page)search.Results.First(res => res is Page));
//        }
//        else
//        {
//            logger.LogWarning("Could not find Notion page for member: {}", member.Username);
//            messageLogger.LogWarning("Could not find Notion page for member: {}", member.Username);
//        }

//        await SyncRoles(member: member, page: page, dryRun: dryRun);
//    }

//    private async Task<DiscordMember?> SyncRoles(DiscordMember? member = null, MemberPage? page = null, bool dryRun = true)
//    {
//        HashSet<DiscordRole> newRoles = [];
//        DiscordRole inactiveRole = _discordRoleCache.Values.First(role => role.Id == options.Value.InactiveRoleId);

//        // If only member is given, mark inactive
//        if (page == null && member is not null)
//        {
//            newRoles.Add(inactiveRole);
//        }
//        // If page is given, get roles from page and member from page (if null)
//        else if (page != null)
//        {
//            member ??= await GetMember(page);
//            if (member is null)
//            {
//                return null;
//            }
//            newRoles.UnionWith(await GetRoles(page));
//        }
//        // If neither are given, give up
//        else
//        {
//            throw new ArgumentNullException("Both `member` and `page` cannot be null", innerException: null);
//        }

//        // Don't affect member's whose roles are safe, or who have roles
//        // belonging to bots.
//        if (member.Roles.Any(role => options.Value.SafeRoleIds.Contains(role.Id) || role.IsManaged))
//        {
//            logger.LogDebug("Member ignored during sync: `{}` (`{}`)", member.Username, member.DisplayName);
//            messageLogger.LogDebug("Member ignored during sync: `{}` (`{}`)", member.Username, member.DisplayName);
//            return member;
//        }

//        // Add any roles the bot cant touch
//        int highestRole = discordService.MainGuild.CurrentMember.Roles.Max(role => role.Position);
//        newRoles.UnionWith(member.Roles.Where(
//            role => role.Position >= highestRole || options.Value.IgnoredRoleIds.Contains(role.Id)
//        ));

//        string logString = string.Join("`, `", newRoles.Select(role => role.Name));
//        logger.LogDebug(
//            "(dry run: `{}`) Roles for user `{}` (`{}`): `{}`",
//            dryRun,
//            member.Username,
//            member.DisplayName,
//            logString
//        );
//        messageLogger.LogDebug(
//            "(dry run: `{}`) Roles for user `{}` (`{}`): `{}`",
//            dryRun,
//            member.Username,
//            member.DisplayName,
//            logString
//        );

//        // Change the member's roles if new roles don't match
//        if (!dryRun && !newRoles.SetEquals(member.Roles))
//        {
//            await member.ModifyAsync(model => model.Roles = newRoles.ToList());
//            await Task.Delay(1000);
//        }

//        // Notify member if they are newly marked as inactive
//        if (!dryRun && newRoles.Contains(inactiveRole) && !member.Roles.Contains(inactiveRole))
//        {
//            try
//            {
//                await member.SendMessageAsync(options.Value.InactiveMessage);
//                logger.LogDebug("Member sent inactive message: `{}` (`{}`)", member.Username, member.DisplayName);
//                messageLogger.LogDebug("Member sent inactive message: `{}` (`{}`)", member.Username, member.DisplayName);
//            }
//            catch (Exception ex)
//            {
//                logger.LogDebug(
//                    "Failed to send member `{}` (`{}`) inactive message sent inactive message: {}",
//                    member.Username,
//                    member.DisplayName,
//                    ex
//                );
//                messageLogger.LogDebug(
//                    "Failed to send member `{}` (`{}`) inactive message sent inactive message: {}",
//                    member.Username,
//                    member.DisplayName,
//                    ex
//                );
//            }
//            finally
//            {
//                await Task.Delay(3000);
//            }
//        }

//        return member;
//    }

//    private async Task<DiscordMember?> GetMember(MemberPage page)
//    {
//        if (string.IsNullOrWhiteSpace(page.DiscordUsername))
//        {
//            logger.LogWarning("User with page url {} has no discord.", page.Url);
//            messageLogger.LogWarning("User with page url {} has no discord.", page.Url);
//            return null;
//        }

//        // Remove superfluous characters that only mess up search
//        string username = page.DiscordUsername.TrimStart('@').ToLower();

//        // Check cache for user
//        DiscordMember? member = discordService.MainGuild.Members.Values
//            .FirstOrDefault(member => member.Username == username);

//        // If not in cache, search the guild
//        if (member is null)
//        {
//            IReadOnlyList<DiscordMember> search = await discordService.MainGuild.SearchMembersAsync(username);
//            if (search.Count == 0 || search[0].Username != username)
//            {
//                logger.LogWarning("Could not find member: {}", username);
//                messageLogger.LogWarning("Could not find member: {}", username);
//            }
//            else
//            {
//                member = search[0];
//            }
//            await Task.Delay(333);
//        }
//        return member;
//    }

//    #region Role Getting
//    private async Task<IEnumerable<DiscordRole>> GetRoles(MemberPage page)
//    {
//        HashSet<DiscordRole> roles = [];

//        // Get active role
//        DiscordRole? activeRole = GetActiveRole(page);
//        if (activeRole is not null)
//        {
//            roles.Add(activeRole);

//            // If they are inactive, then inactive is the only role they can
//            // have
//            if (activeRole.Id == options.Value.InactiveRoleId)
//            {
//                return roles;
//            }
//        }

//        // Get roles for teams
//        foreach (DiscordRole role in await GetTeams(page))
//        {
//            roles.Add(role);
//        }

//        // Get position roles
//        foreach (string positionName in page.PositionNames)
//        {
//            if (positionName == "Individual Contributor")
//            {
//                continue;
//            }

//            if (_technicalLeadRegex.IsMatch(positionName))
//            {
//                roles.Add(_discordRoleCache.Values.First(role => role.Id == options.Value.TechnicalLeadRoleId));
//            }

//            // Remove team suffix to make roles a little easier to read
//            DiscordRole? positionRole = GetRoleOrDefault(positionName.Replace(" Team", ""));
//            if (positionRole is not null)
//            {
//                roles.Add(positionRole);
//            }
//        }

//        // Get discipline roles
//        foreach (string disciplineName in page.DisciplineNames)
//        {
//            DiscordRole? positionRole = GetRoleOrDefault(disciplineName);
//            if (positionRole is not null)
//            {
//                roles.Add(positionRole);
//            }
//        }
//        return roles;
//    }

//    private DiscordRole? GetActiveRole(MemberPage page)
//    {
//        if (page.ActiveStatus == "Active")
//        {
//            return null;
//        }
//        else
//        {
//            return GetRoleOrDefault(page.ActiveStatus);
//        }
//    }

//    private async Task<IEnumerable<DiscordRole>> GetTeams(MemberPage page)
//    {
//        List<DiscordRole> roles = [];
//        foreach (string teamId in page.TeamIds)
//        {
//            string teamName = await GetTeamName(teamId);

//            // Remove team suffix to make roles a little easier to read
//            DiscordRole? role = GetRoleOrDefault(teamName.Replace(" Team", ""));
//            if (role is not null)
//            {
//                roles.Add(role);
//            }
//        }

//        return roles;
//    }

//    private async Task<string> GetTeamName(string id)
//    {
//        // Try using our cached property id if it exists
//        if (_teamPageNamePropertyId is not null)
//        {
//            try
//            {
//                ListPropertyItem item = (await notionClient.Pages.RetrievePagePropertyItemAsync(new RetrievePropertyItemParameters()
//                {
//                    PageId = id,
//                    PropertyId = _teamPageNamePropertyId
//                }) as ListPropertyItem)!;
//                return (item.Results.First() as TitlePropertyItem)!.Title.PlainText;
//            }
//            catch (NotionApiException ex)
//            {
//                // If not found, then move on to rest of method
//                // If another error, rethrow the error to move it up the
//                // stack
//                if (ex.NotionAPIErrorCode != NotionAPIErrorCode.ObjectNotFound)
//                {
//                    throw;
//                }
//            }
//        }

//        // If it doesn't or fails, then recache it and get the whole page
//        Page teamPage = await notionClient.Pages.RetrieveAsync(id);
//        TitlePropertyValue nameValue = (teamPage.Properties[options.Value.TeamNamePropertyName]
//            as TitlePropertyValue)!;
//        _teamPageNamePropertyId = nameValue.Id;
//        return nameValue.Title[0].PlainText;
//    }

//    /// <summary>
//    /// Gets a role with <paramref name="roleName"/> or null if it cannot find
//    /// it. Also will log a warning if role is not found.
//    /// </summary>
//    /// <param name="roleName">The name of the role to find</param>
//    /// <returns>The role with a matching name, or null</returns>
//    private DiscordRole? GetRoleOrDefault(string roleName)
//    {
//        if (!_discordRoleCache.TryGetValue(roleName, out DiscordRole? role))
//        {
//            logger.LogWarning("Could not find role with name: {}", roleName);
//            messageLogger.LogWarning("Could not find role with name: {}", roleName);
//        }

//        return role;
//    }
//    #endregion

//    /// <inheritdoc/>
//    public static void Register(IHostApplicationBuilder builder)
//    {
//        builder.Services
//            .AddInjectableHostedService<RoleSyncer>()
//            .ConfigureTypeSection<RoleSyncerOptions>(builder.Configuration);
//    }

//    /// <summary>
//    /// A class representing a member's page that's just easier to use
//    /// </summary>
//    public class MemberPage(RoleSyncerOptions options, Page notionPage)
//    {
//        public string? TaskName { get; } = (notionPage.Properties[options.DiscordUsernamePropertyName] as TitlePropertyValue)
//            !.Title[0].PlainText;
//        public string Reviewer { get; } = (notionPage.Properties[options.ActivePropertyName] as SelectPropertyValue)
//            !.Select.Name;
//        public string[] PositionNames { get; } = (notionPage.Properties[options.ClubPositionsPropertyName] as MultiSelectPropertyValue)
//            !.MultiSelect.Select(option => option.Name).ToArray();
//        public string[] DisciplineNames { get; } = (notionPage.Properties[options.DisciplinesPropertyName] as MultiSelectPropertyValue)
//            !.MultiSelect.Select(option => option.Name).ToArray();

//        /// <summary>
//        /// The PAGE IDS, not the name, of the teams the user is on.
//        /// </summary>
//        public string[] TeamIds { get; } = (notionPage.Properties[options.TeamsPropertyName] as RelationPropertyValue)
//            !.Relation.Select(id => id.Id).ToArray();

//        /// <summary>
//        /// The URL pointing to the notion page.
//        /// </summary>
//        public string Url { get; } = notionPage.Url;
//    }
//}

