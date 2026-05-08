using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SocialMaster.Models;

public class SocialConfig
{
    [JsonProperty("version")]
    public string Version { get; set; } = "2.0.0";

    [JsonProperty("settings")]
    public GlobalSettings Settings { get; set; } = new();

    [JsonProperty("platforms")]
    public Dictionary<string, PlatformConfig> Platforms { get; set; } = new();

    [JsonProperty("sources")]
    public Dictionary<string, JObject> Sources { get; set; } = new();

    [JsonProperty("ui")]
    public UiConfig Ui { get; set; } = new();
}

public class GlobalSettings
{
    [JsonProperty("accounts_directory")]
    public string AccountsDirectory { get; set; } = "accounts";
}

public class PlatformConfig
{
    [JsonProperty("base_url")]
    public string BaseUrl { get; set; } = "";

    [JsonProperty("upload_timeout_seconds")]
    public int UploadTimeoutSeconds { get; set; } = 120;

    [JsonProperty("navigation_timeout_seconds")]
    public int NavigationTimeoutSeconds { get; set; } = 60;

    [JsonProperty("default_min_interval_hours")]
    public double DefaultMinIntervalHours { get; set; } = 3;

    [JsonProperty("default_max_interval_hours")]
    public double DefaultMaxIntervalHours { get; set; } = 8;

    [JsonProperty("default_caption_hashtags")]
    public List<string> DefaultCaptionHashtags { get; set; } = new();

    [JsonProperty("account_nurturing")]
    public NurturingConfig AccountNurturing { get; set; } = new();
}

public class NurturingConfig
{
    [JsonProperty("scroll_interval_min_minutes")]
    public double ScrollIntervalMinMinutes { get; set; } = 5;

    [JsonProperty("scroll_interval_max_minutes")]
    public double ScrollIntervalMaxMinutes { get; set; } = 15;

    [JsonProperty("scroll_distance_pixels")]
    public int ScrollDistancePixels { get; set; } = 300;

    [JsonProperty("scroll_variation_pixels")]
    public int ScrollVariationPixels { get; set; } = 100;
}

public class UiConfig
{
    [JsonProperty("window_width")]
    public int WindowWidth { get; set; } = 1100;

    [JsonProperty("window_height")]
    public int WindowHeight { get; set; } = 650;

    [JsonProperty("update_interval_seconds")]
    public int UpdateIntervalSeconds { get; set; } = 5;
}
