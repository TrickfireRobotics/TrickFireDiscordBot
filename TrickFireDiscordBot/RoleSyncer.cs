using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Notion.Client;
using System.Net;
using TrickFireDiscordBot.Discord;
using TrickFireDiscordBot.Notion;

namespace TrickFireDiscordBot
{
    public class RoleSyncer(ILogger logger, NotionClient notionClient, DiscordBot discordBot, WebhookListener listener)
    {
        public const string WebhookEndpoint = "/members";

        public ILogger Logger { get; } = logger;
        public NotionClient NotionClient { get; } = notionClient;
        public DiscordBot DiscordBot { get; } = discordBot;
        public WebhookListener WebhookListener { get; } = listener;

        private DiscordGuild? _trickFireGuild;
        private readonly Dictionary<string, DiscordRole> _discordRoleCache = [];
        // This could be a potential memory leak, but the rate at which it grows
        // is so slow it will not be an issue
        private readonly Dictionary<ObjectId, string> _pageNameCache = [];
        private string? _teamPageNamePropertyId = null;

        public async Task Start()
        {
            WebhookListener.OnWebhookReceived += OnWebhook;

            _trickFireGuild = await DiscordBot.Client.GetGuildAsync(Config.Instance.TrickfireGuild);
            foreach (DiscordRole role in _trickFireGuild.Roles.Values)
            {
                _discordRoleCache.Add(role.Name, role);
            }
        }

        public void Stop()
        {
            WebhookListener.OnWebhookReceived -= OnWebhook;
        }

        private async void OnWebhook(HttpListenerRequest request)
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
                Logger.LogWarning("Could not parse automation: {}", json);
                return;
            }

            await SyncRoles(page);

            Console.WriteLine(JsonConvert.SerializeObject(page, Formatting.Indented));
        }

        private async Task SyncRoles(Page notionPage)
        {
            DiscordMember? member = GetMember(notionPage);
            if (member is null)
            {
                return;
            }
            //await member.ReplaceRolesAsync(await GetRoles(notionPage));
            Logger.LogInformation(member.DisplayName);
            foreach (DiscordRole role in await GetRoles(notionPage)) 
            {
                Logger.LogInformation(role.Name);
            }
        }

        private DiscordMember? GetMember(Page notionPage)
        {
            if (_trickFireGuild is null)
            {
                return null;
            }

            // We want this to fail hard if something is wrong
            string username = (notionPage.Properties["Discord Username"] as PhoneNumberPropertyValue)!.PhoneNumber;
            DiscordMember? member = _trickFireGuild.Members.Values
                .FirstOrDefault(member => member.Username == username);

            if (member is null)
            {
                Logger.LogWarning("Could not find member: {}", username);
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

            MultiSelectPropertyValue positions = (notionPage.Properties["Club Position(s)"] as MultiSelectPropertyValue)!;
            foreach (SelectOption item in positions.MultiSelect)
            {
                DiscordRole? role = GetRoleOrDefault(item.Name);
                if (role is not null)
                {
                    roles.Add(role);
                }
            }

            return roles;
        }

        private DiscordRole? GetActiveRole(Page notionPage)
        {
            string activeValue = (notionPage.Properties["Active?"] as SelectPropertyValue)!.Select.Name;
            if (activeValue == "Inactive")
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
            List<ObjectId> teamIds = (notionPage.Properties["Teams"] as RelationPropertyValue)!.Relation;
            foreach (ObjectId id in teamIds)
            {
                string teamName = await GetTeamName(id);
                
                DiscordRole? role = GetRoleOrDefault(teamName);
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
            TitlePropertyValue nameValue;
            if (_teamPageNamePropertyId is not null)
            {
                try
                {
                    nameValue = (await NotionClient.Pages.RetrievePagePropertyItemAsync(new RetrievePropertyItemParameters()
                    {
                        PageId = id.Id,
                        PropertyId = _teamPageNamePropertyId
                    }) as TitlePropertyValue)!;
                    return nameValue.Title[0].PlainText;
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
            Page teamPage = await NotionClient.Pages.RetrieveAsync(id.Id);
            nameValue = (teamPage.Properties["Name"] as TitlePropertyValue)!;
            _teamPageNamePropertyId = nameValue.Id;
            return nameValue.Title[0].PlainText;
        }

        private DiscordRole? GetRoleOrDefault(string roleName)
        {
            if (!_discordRoleCache.TryGetValue(roleName, out DiscordRole? role))
            {
                Logger.LogWarning("Could not find role with name: {}", roleName);
            }

            return role;
        }
    }
}
