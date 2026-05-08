namespace SocialMaster.Abstractions;

public class ContentItem
{
    public string FilePath { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string MimeType { get; set; } = "video/mp4";
    public string SourceType { get; set; } = "";
}
