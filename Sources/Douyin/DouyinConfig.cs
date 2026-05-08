using Newtonsoft.Json;

namespace SocialMaster.Sources.Douyin;

public class DouyinConfig
{
    [JsonProperty("api_host")]
    public string ApiHost { get; set; } = "api.tikhub.io";

    [JsonProperty("auth_token")]
    public string AuthToken { get; set; } = "";

    [JsonProperty("download_dir")]
    public string DownloadDir { get; set; } = "downloads";

    [JsonProperty("min_file_size_mb")]
    public double MinFileSizeMb { get; set; } = 1.0;

    [JsonProperty("download_timeout_seconds")]
    public int DownloadTimeoutSeconds { get; set; } = 180;

    [JsonProperty("page_size")]
    public int PageSize { get; set; } = 20;

    [JsonProperty("max_attempts_per_page")]
    public int MaxAttemptsPerPage { get; set; } = 3;
}
