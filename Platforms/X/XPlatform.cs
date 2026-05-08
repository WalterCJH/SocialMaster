using Microsoft.Playwright;
using SocialMaster.Abstractions;
using SocialMaster.Helpers;
using SocialMaster.Models;

namespace SocialMaster.Platforms.X;

public class XPlatform : ISocialPlatform
{
    public string PlatformName => "X";

    private readonly PlatformConfig _cfg;
    private readonly Random _rng = new();

    public XPlatform(PlatformConfig config)
    {
        _cfg = config;
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    // 開啟首頁，偵測導覽列的 Home 圖示是否存在，以判斷目前是否已登入
    // 重用 Playwright 啟動時建立的初始分頁，避免額外開關分頁
    public async Task<bool> IsLoggedInAsync(IBrowserContext context)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        try
        {
            await page.GotoAsync(_cfg.BaseUrl, new PageGotoOptions
            {
                Timeout = _cfg.NavigationTimeoutSeconds * 1000,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });
            return await FindAnyAsync(page, XSelectors.NavHomeIcon) != null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("-", PlatformName, $"IsLoggedIn 檢查失敗: {ex.Message}");
            return false;
        }
    }

    // 開啟首頁等待人工登入，每 3 秒輪詢一次是否偵測到 Home 圖示，成功後繼續執行
    public async Task WaitForLoginAsync(IBrowserContext context, CancellationToken ct)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        await page.GotoAsync(_cfg.BaseUrl, new PageGotoOptions
        {
            Timeout = _cfg.NavigationTimeoutSeconds * 1000,
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        AppLogger.Info("-", PlatformName, "瀏覽器已開啟，請手動登入 X...");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(3000, ct);
            try
            {
                if (await FindAnyAsync(page, XSelectors.NavHomeIcon) != null)
                {
                    AppLogger.Info("-", PlatformName, "偵測到登入成功，繼續執行");
                    return;
                }
            }
            catch { /* page may be navigating, retry */ }
        }
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    // 執行完整的 X 發文流程：關閉彈窗 → 點擊發文按鈕 → 輸入說明文字 → 附加影片檔 → 等待上傳完成 → 點擊 Post 發布
    public async Task<bool> UploadContentAsync(IBrowserContext context, ContentItem item, CancellationToken ct)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        try
        {
            await page.GotoAsync(_cfg.BaseUrl, new PageGotoOptions
            {
                Timeout = _cfg.NavigationTimeoutSeconds * 1000,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            await DismissPopupsAsync(page);

            // Step 1: click the compose button
            var composeBtn = await FindAnyAsync(page, XSelectors.ComposeButton, timeoutMs: 8000);
            if (composeBtn == null)
            {
                await SaveDebugScreenshot(page, "no_compose_btn");
                AppLogger.Error("-", PlatformName, "找不到發文按鈕，已截圖");
                return false;
            }
            await composeBtn.ClickAsync();
            AppLogger.Info("-", PlatformName, "已點擊發文按鈕");
            await Task.Delay(1000, ct);

            // Step 2: fill tweet text
            var caption = BuildCaption(item.Description);
            AppLogger.Info("-", PlatformName, $"Caption ({caption.Length}字): {(caption.Length > 60 ? caption[..60] + "…" : caption)}");

            var textArea = await FindAnyAsync(page, XSelectors.TweetTextArea, timeoutMs: 5000);
            if (textArea != null)
            {
                var box = await textArea.BoundingBoxAsync();
                if (box != null)
                {
                    var cx = box.X + box.Width / 2;
                    var cy = box.Y + box.Height / 2;
                    await page.Mouse.MoveAsync(cx, cy);
                    await Task.Delay(100, ct);
                    await page.Mouse.ClickAsync(cx, cy);
                    await Task.Delay(200, ct);
                }

                var lines = caption.Split('\n');
                for (int li = 0; li < lines.Length; li++)
                {
                    if (lines[li].Length > 0)
                        await page.Keyboard.TypeAsync(lines[li], new KeyboardTypeOptions { Delay = 20 });
                    if (li < lines.Length - 1)
                    {
                        await page.Keyboard.PressAsync("Shift+Enter");
                        await Task.Delay(50, ct);
                    }
                }
                AppLogger.Info("-", PlatformName, "已輸入推文文字");
            }
            else
            {
                AppLogger.Warn("-", PlatformName, "找不到文字輸入框，略過文字");
            }

            await Task.Delay(500, ct);

            // Step 3: attach video via hidden file input
            var fileChooserTask = page.WaitForFileChooserAsync(
                new PageWaitForFileChooserOptions { Timeout = 10000 });

            var mediaBtn = await FindAnyAsync(page, XSelectors.MediaButton, timeoutMs: 5000);
            if (mediaBtn == null)
            {
                // Fallback: directly trigger the hidden file input
                var fileInput = await page.QuerySelectorAsync(XSelectors.FileInput);
                if (fileInput != null)
                    await fileInput.ClickAsync();
                else
                {
                    await SaveDebugScreenshot(page, "no_media_btn");
                    AppLogger.Error("-", PlatformName, "找不到媒體按鈕，已截圖");
                    return false;
                }
            }
            else
            {
                await mediaBtn.ClickAsync();
            }

            var fileChooser = await fileChooserTask;
            await fileChooser.SetFilesAsync(item.FilePath);
            AppLogger.Info("-", PlatformName, "已選擇影片檔案，等待上傳...");

            // Step 4: wait for upload progress to finish
            var progressBar = await FindAnyAsync(page, XSelectors.UploadProgressBar, timeoutMs: 5000);
            if (progressBar != null)
            {
                AppLogger.Info("-", PlatformName, "影片上傳中...");
                // Wait until progress bar disappears (upload complete)
                try
                {
                    var combined = string.Join(", ", XSelectors.UploadProgressBar);
                    await page.WaitForSelectorAsync(combined, new PageWaitForSelectorOptions
                    {
                        State = WaitForSelectorState.Hidden,
                        Timeout = _cfg.UploadTimeoutSeconds * 1000,
                    });
                }
                catch
                {
                    AppLogger.Warn("-", PlatformName, "等待上傳完成超時，嘗試繼續");
                }
            }
            else
            {
                // Give time for upload even without progress bar detection
                await Task.Delay(5000, ct);
            }
            AppLogger.Info("-", PlatformName, "影片上傳完成，準備發文");

            // Step 5: click Post button
            await Task.Delay(500, ct);
            var postLocator = page.Locator("button, [role='button']")
                .Filter(new LocatorFilterOptions { HasText = "發文" });
            if (await postLocator.CountAsync() == 0)
                postLocator = page.Locator("button, [role='button']")
                    .Filter(new LocatorFilterOptions { HasText = "Post" });
            if (await postLocator.CountAsync() == 0)
                postLocator = page.Locator("[data-testid='tweetButtonInline'], [data-testid='tweetButton']");

            if (await postLocator.CountAsync() > 0)
            {
                await postLocator.First.ScrollIntoViewIfNeededAsync();
                await postLocator.First.ClickAsync();
                AppLogger.Info("-", PlatformName, "已點擊發文按鈕");
            }
            else
            {
                await SaveDebugScreenshot(page, "no_post_btn");
                AppLogger.Error("-", PlatformName, "找不到發文按鈕，已截圖");
                return false;
            }

            // Step 6: wait for success toast
            var success = await FindAnyAsync(page, XSelectors.UploadSuccess,
                timeoutMs: _cfg.UploadTimeoutSeconds * 1000);

            await Task.Delay(2000, ct);
            AppLogger.Info("-", PlatformName, success != null ? "上傳成功" : "上傳結果不確定（未偵測到成功訊息）");
            return success != null;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await SaveDebugScreenshot(page, "upload_error");
            AppLogger.Error("-", PlatformName, "上傳失敗", ex);
            return false;
        }
    }

    // ── Nurture ───────────────────────────────────────────────────────────────

    // 模擬真人使用行為：開啟首頁隨機捲動一段時間，偶爾觸發長暫停，降低帳號被平台識別為機器人的風險
    public async Task NurtureAsync(IBrowserContext context, CancellationToken ct)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_cfg.BaseUrl}/home", new PageGotoOptions
            {
                Timeout = _cfg.NavigationTimeoutSeconds * 1000,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            var nurture = _cfg.AccountNurturing;

            var endTime = DateTime.Now.AddMinutes(RandomBetween(nurture.ScrollIntervalMinMinutes, nurture.ScrollIntervalMaxMinutes));

            AppLogger.Info("-", PlatformName, $"模擬人類行為於 {endTime:yyyy/MM/dd HH:mm:ss} 結束");

            while (DateTime.Now < endTime && !ct.IsCancellationRequested)
            {
                var distance = nurture.ScrollDistancePixels
                    + _rng.Next(-nurture.ScrollVariationPixels, nurture.ScrollVariationPixels);
                await page.EvaluateAsync($"window.scrollBy(0, {distance})");
                await Task.Delay(TimeSpan.FromSeconds(RandomBetween(1.5, 4.0)), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error("-", PlatformName, "Nurture 發生錯誤", ex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // 嘗試關閉頁面上出現的通知或促銷彈窗，最多重複 3 次直到沒有可關閉的按鈕
    private static async Task DismissPopupsAsync(IPage page)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            var btn = await FindAnyAsync(page, XSelectors.PopupDismissButtons, timeoutMs: 2000);
            if (btn == null) break;
            try
            {
                await btn.ClickAsync();
                await Task.Delay(800);
            }
            catch { break; }
        }
    }

    // 等待一組 selector 中任一個變為可見，回傳第一個找到的元素；逾時則回傳 null
    private static async Task<IElementHandle?> FindAnyAsync(
        IPage page, string[] selectors, int timeoutMs = 5000)
    {
        var combined = string.Join(", ", selectors);
        try
        {
            return await page.WaitForSelectorAsync(combined,
                new PageWaitForSelectorOptions { Timeout = timeoutMs, State = WaitForSelectorState.Visible });
        }
        catch
        {
            return null;
        }
    }

    // 截取整頁截圖並存至 debug/ 資料夾，用於發生錯誤時的問題診斷
    private static async Task SaveDebugScreenshot(IPage page, string tag)
    {
        try
        {
            Directory.CreateDirectory("debug");
            var path = $"debug/x_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        }
        catch { }
    }

    // 將影片說明文字與設定的 hashtag 組合成完整推文內容（X 有 280 字元限制，超出由平台自行截斷）
    private string BuildCaption(string description)
    {
        var tags = string.Join(" ", _cfg.DefaultCaptionHashtags);
        if (string.IsNullOrWhiteSpace(description)) return tags;
        // X has a 280 char limit — keep full text but note the constraint
        var full = $"{description}\n\n{tags}";
        return full;
    }

    // 在 min 與 max 之間產生均勻分布的隨機浮點數
    private double RandomBetween(double min, double max) =>
        min + (max - min) * _rng.NextDouble();
}
