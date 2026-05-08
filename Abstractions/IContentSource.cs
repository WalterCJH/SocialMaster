namespace SocialMaster.Abstractions;

public interface IContentSource
{
    // 來源名稱，如 "Douyin"
    string SourceName { get; }

    // 從來源取得下一筆尚未下載的內容；無新內容或發生錯誤時回傳 null
    Task<ContentItem?> GetNextContentAsync(string sourceConfig, CancellationToken ct);
}
