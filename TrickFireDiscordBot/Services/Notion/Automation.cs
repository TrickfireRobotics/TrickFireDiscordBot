using Newtonsoft.Json;
using Notion.Client;

namespace TrickFireDiscordBot.Services.Notion;

public class Automation
{
    [JsonProperty("source")]
    public AutomationSource? Source { get; set; } = null;

    [JsonProperty("data")]
    public IObject? Data { get; set; } = null;
}

public class AutomationSource
{
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("automation_id")]
    public string AutomationId { get; set; } = "";


    [JsonProperty("action_id")]
    public string ActionId { get; set; } = "";

    [JsonProperty("event_id")]
    public string EventId { get; set; } = "";

    [JsonProperty("attempt")]
    public int Attempt { get; set; } = 0;
}
