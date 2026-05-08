using Newtonsoft.Json;

namespace SocialMaster.Models;

public class ContentLog
{
    [JsonProperty("entries")]
    public List<ContentLogEntry> Entries { get; set; } = new();
}

public class ContentLogEntry
{
    [JsonProperty("source_id")]
    public string SourceId { get; set; } = "";

    [JsonProperty("source_type")]
    public string SourceType { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("local_path")]
    public string LocalPath { get; set; } = "";

    [JsonProperty("downloaded_at")]
    public DateTime DownloadedAt { get; set; }

    [JsonProperty("uploaded")]
    public bool Uploaded { get; set; }

    [JsonProperty("uploaded_at")]
    public DateTime? UploadedAt { get; set; }

    [JsonProperty("upload_attempts")]
    public int UploadAttempts { get; set; }
}
