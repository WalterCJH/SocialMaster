using Newtonsoft.Json;

namespace SocialMaster.Models;

public class AccountProfile
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("platform")]
    public string Platform { get; set; } = "Instagram";

    [JsonProperty("source_type")]
    public string SourceType { get; set; } = "Douyin";

    [JsonProperty("source_config")]
    public string SourceConfig { get; set; } = "";

    [JsonProperty("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonProperty("min_interval_minutes")]
    public int MinIntervalMinutes { get; set; } = 1150;

    [JsonProperty("max_interval_minutes")]
    public int MaxIntervalMinutes { get; set; } = 5250;

    [JsonProperty("custom_name")]
    public string CustomName { get; set; } = "";

    [JsonProperty("notes")]
    public string Notes { get; set; } = "";

    [JsonProperty("facebook_page_url")]
    public string FacebookPageUrl { get; set; } = "";

    // Computed from the directory this file lives in — not persisted
    [JsonIgnore]
    public string AccountDir { get; set; } = "";

    [JsonIgnore]
    public string BrowserDir => Path.Combine(AccountDir, "browser");

    [JsonIgnore]
    public string VideosDir => Path.Combine(AccountDir, "videos");
}
