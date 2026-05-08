using Microsoft.Playwright;

namespace SocialMaster.Abstractions;

public interface ISocialPlatform
{
    // 平台名稱，如 "Instagram" 或 "X"
    string PlatformName { get; }

    // 偵測目前 BrowserContext 是否已登入該平台
    Task<bool> IsLoggedInAsync(IBrowserContext context);

    // 開啟登入頁面並輪詢，直到偵測到登入成功或取消
    Task WaitForLoginAsync(IBrowserContext context, CancellationToken ct);

    // 將 ContentItem 上傳至平台；成功回傳 true，失敗回傳 false
    Task<bool> UploadContentAsync(IBrowserContext context, ContentItem item, CancellationToken ct);

    // 模擬真人瀏覽行為（滑動動態），降低帳號被識別為機器人的風險
    Task NurtureAsync(IBrowserContext context, CancellationToken ct);
}
