using Microsoft.Playwright;
using SocialMaster.Abstractions;
using SocialMaster.Helpers;
using SocialMaster.Models;

namespace SocialMaster.Platforms.Facebook;

public class FacebookPlatform : ISocialPlatform
{
    public string PlatformName => "Facebook";

    private readonly PlatformConfig _cfg;
    private readonly string _fanPageUrl;
    private readonly Random _rng = new();

    public FacebookPlatform(PlatformConfig config, string fanPageUrl = "")
    {
        _cfg = config;
        _fanPageUrl = fanPageUrl;
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    // 開啟 Facebook 首頁，偵測導覽列的使用者連結是否存在，以判斷目前是否已登入
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
            return await FindAnyAsync(page, FacebookSelectors.NavHomeIcon) != null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("-", PlatformName, $"IsLoggedIn 檢查失敗: {ex.Message}");
            return false;
        }
    }

    // 開啟 Facebook 首頁等待人工登入，每 3 秒輪詢一次，偵測到登入後繼續執行
    public async Task WaitForLoginAsync(IBrowserContext context, CancellationToken ct)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        await page.GotoAsync(_cfg.BaseUrl, new PageGotoOptions
        {
            Timeout = _cfg.NavigationTimeoutSeconds * 1000,
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        AppLogger.Info("-", PlatformName, "瀏覽器已開啟，請手動登入 Facebook...");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(3000, ct);
            try
            {
                if (await FindAnyAsync(page, FacebookSelectors.NavHomeIcon) != null)
                {
                    AppLogger.Info("-", PlatformName, "偵測到登入成功，繼續執行");
                    return;
                }
            }
            catch { /* page may be navigating, retry */ }
        }
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    // 執行完整的 Facebook Reel 上傳流程：
    //   個人登入 → 導覽至粉絲團 → 點擊 Reel 按鈕 → 選擇影片 → 等待上傳 → 透過精靈步驟填寫說明 → 分享
    public async Task<bool> UploadContentAsync(IBrowserContext context, ContentItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_fanPageUrl))
        {
            AppLogger.Error("-", PlatformName, "粉絲團網址未設定，請在帳號設定中填寫「粉絲團網址 (FB)」");
            return false;
        }

        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        try
        {
            // Step 1: Navigate to fan page
            await page.GotoAsync(_fanPageUrl, new PageGotoOptions
            {
                Timeout = _cfg.NavigationTimeoutSeconds * 1000,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });
            AppLogger.Info("-", PlatformName, $"已導覽至粉絲團: {_fanPageUrl}");

            await Task.Delay(1500, ct);

            // Step 1b: Click "立即切換" to enter page management view
            // Facebook prompts to switch from personal account to page context when visiting a managed page
            await SwitchToPageContextAsync(page, ct);

            await DismissPopupsAsync(page);
            await Task.Delay(1500, ct);

            // Step 2: Click Reel tab → wait 2s → click "建立 Reel" button
            var reelClicked = await ClickReelButtonAsync(page, ct);
            if (!reelClicked)
            {
                await SaveDebugScreenshot(page, "no_reel_btn");
                AppLogger.Error("-", PlatformName, "找不到 Reel 分頁或建立 Reel 按鈕，已截圖至 debug/");
                return false;
            }
            AppLogger.Info("-", PlatformName, "已點擊建立 Reel 按鈕，等待上傳介面出現...");
            await Task.Delay(2000, ct);

            // Step 3: Upload video — intercept file chooser before clicking the upload trigger
            IFileChooser fileChooser;
            try
            {
                var fileChooserTask = page.WaitForFileChooserAsync(
                    new PageWaitForFileChooserOptions { Timeout = 15000 });

                var uploadTriggered = await TriggerVideoUploadAsync(page);
                if (!uploadTriggered)
                {
                    await SaveDebugScreenshot(page, "no_upload_trigger");
                    AppLogger.Error("-", PlatformName, "無法觸發影片上傳，已截圖至 debug/");
                    return false;
                }

                fileChooser = await fileChooserTask;
            }
            catch (Exception ex)
            {
                await SaveDebugScreenshot(page, "file_chooser_fail");
                AppLogger.Error("-", PlatformName, $"等待檔案選擇器失敗: {ex.Message}");
                return false;
            }

            await fileChooser.SetFilesAsync(item.FilePath);
            AppLogger.Info("-", PlatformName, "已選擇影片，等待上傳...");

            // Step 4: Wait for upload progress bar, then wait for it to disappear
            var progressBar = await FindAnyAsync(page, FacebookSelectors.UploadProgressBar, timeoutMs: 10000);
            if (progressBar != null)
            {
                AppLogger.Info("-", PlatformName, "影片上傳中...");
                try
                {
                    var combined = string.Join(", ", FacebookSelectors.UploadProgressBar);
                    await page.WaitForSelectorAsync(combined, new PageWaitForSelectorOptions
                    {
                        State = WaitForSelectorState.Hidden,
                        Timeout = _cfg.UploadTimeoutSeconds * 1000,
                    });
                }
                catch
                {
                    AppLogger.Warn("-", PlatformName, "等待上傳完成超時，繼續流程");
                }
            }
            else
            {
                // No progress bar detected — give extra time for processing
                await Task.Delay(10000, ct);
            }
            AppLogger.Info("-", PlatformName, "影片上傳完成，等待「繼續」按鈕...");

            // Step 4b: After upload, click "繼續" to proceed past the video preview step
            await Task.Delay(2000, ct);
            var continued = await ClickContinueButtonAsync(page);
            if (continued)
            {
                AppLogger.Info("-", PlatformName, "已點擊「繼續」，等待下一步...");
                await Task.Delay(1500, ct);
            }
            else
            {
                AppLogger.Warn("-", PlatformName, "未偵測到「繼續」按鈕，繼續流程");
            }

            // Step 5: Navigate any remaining wizard steps until Details (caption) step is reached
            var captionEl = await NavigateToDetailsStepAsync(page, ct);

            // Step 6: Fill caption
            var caption = BuildCaption(item.Description);
            AppLogger.Info("-", PlatformName,
                $"Caption ({caption.Length}字): {(caption.Length > 60 ? caption[..60] + "…" : caption)}");

            if (captionEl != null)
            {
                await FillCaptionAsync(page, context, captionEl, caption, ct);
            }
            else
            {
                AppLogger.Warn("-", PlatformName, "找不到說明文字欄位，略過");
                await SaveDebugScreenshot(page, "no_caption_el");
            }

            await Task.Delay(1500, ct);

            // Step 6b: Click "繼續" again after caption — required to reveal the "發佈" button
            var continued2 = await ClickContinueButtonAsync(page);
            if (continued2)
            {
                AppLogger.Info("-", PlatformName, "已點擊第二次「繼續」，等待發佈按鈕...");
                await Task.Delay(1500, ct);
            }
            else
            {
                AppLogger.Warn("-", PlatformName, "未偵測到第二次「繼續」按鈕，繼續流程");
            }

            // Step 7: Click "發佈" button
            var published = await ClickPublishButtonAsync(page);
            if (!published)
            {
                await SaveDebugScreenshot(page, "no_publish_btn");
                AppLogger.Error("-", PlatformName, "找不到分享按鈕，已截圖至 debug/");
                return false;
            }

            AppLogger.Info("-", PlatformName, "已點擊發佈按鈕，等待「Reel 正在處理中」確認訊息...");

            // Wait for "你的 Reel 正在處理中" — the text Facebook shows after successful submission
            var success = await FindAnyAsync(page, FacebookSelectors.UploadSuccess,
                timeoutMs: _cfg.UploadTimeoutSeconds * 1000);

            if (success != null)
                AppLogger.Info("-", PlatformName, "偵測到 Reel 發佈成功：「Reel 正在處理中」");
            else
                AppLogger.Warn("-", PlatformName, "未偵測到成功確認訊息，發佈結果不確定");

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

    // 模擬真人使用行為：在 Facebook 首頁隨機捲動一段時間
    public async Task NurtureAsync(IBrowserContext context, CancellationToken ct)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        try
        {
            await page.GotoAsync(_cfg.BaseUrl, new PageGotoOptions
            {
                Timeout = _cfg.NavigationTimeoutSeconds * 1000,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            var nurture = _cfg.AccountNurturing;
            var endTime = DateTime.Now.AddMinutes(
                RandomBetween(nurture.ScrollIntervalMinMinutes, nurture.ScrollIntervalMaxMinutes));

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

    // 粉絲頁管理員到訪時，Facebook 會顯示「立即切換」提示以進入頁面管理視角
    // 切換後可能出現「歡迎使用你的新版粉絲專頁！」popup，需點擊「使用粉絲專頁」關閉
    private static async Task SwitchToPageContextAsync(IPage page, CancellationToken ct)
    {
        var switched = await page.EvaluateAsync<bool>(@"
            () => {
                const labels = ['立即切換', 'Switch Now', 'Switch now'];
                const all = [...document.querySelectorAll('[role=""button""], button, a')];
                for (const label of labels) {
                    const el = all.find(e => e.textContent.trim() === label);
                    if (el) { el.click(); return true; }
                }
                return false;
            }
        ");

        if (switched)
        {
            AppLogger.Info("-", "Facebook", "已點擊「立即切換」，等待切換至粉絲頁管理視角...");
            await Task.Delay(2500, ct);
        }
        else
        {
            AppLogger.Info("-", "Facebook", "未偵測到「立即切換」提示，繼續執行");
        }

        // 無論是否剛切換，都嘗試關閉「歡迎使用你的新版粉絲專頁！」welcome popup
        var dismissed = await page.EvaluateAsync<bool>(@"
            () => {
                const labels = ['使用粉絲專頁', 'Use Page', 'Use Facebook Page'];
                const all = [...document.querySelectorAll('[role=""button""], button')];
                for (const label of labels) {
                    const el = all.find(e => e.textContent.trim() === label);
                    if (el) { el.click(); return true; }
                }
                return false;
            }
        ");

        if (dismissed)
        {
            AppLogger.Info("-", "Facebook", "已關閉「歡迎使用你的新版粉絲專頁」通知");
            await Task.Delay(1000, ct);
        }
    }

    private static async Task DismissPopupsAsync(IPage page)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            var btn = await FindAnyAsync(page, FacebookSelectors.PopupDismissButtons, timeoutMs: 2000);
            if (btn == null) break;
            try
            {
                await btn.ClickAsync();
                await Task.Delay(800);
            }
            catch { break; }
        }
    }

    // Step A: 點擊粉絲頁 Tab 列中的「Reel」分頁（<a role="tab" href="...reels_tab">）
    // Step B: 等 2 秒後點擊 Reel 區塊內的「建立 Reel」按鈕（<div aria-label="建立 Reel" role="button">）
    private static async Task<bool> ClickReelButtonAsync(IPage page, CancellationToken ct)
    {
        // ── Step A: Click "Reel" tab ──────────────────────────────────────────
        var tabClicked = await page.EvaluateAsync<bool>(@"
            () => {
                // Primary: href contains 'reels_tab' — unique and stable
                const reelTab = document.querySelector('a[role=""tab""][href*=""reels_tab""]');
                if (reelTab) { reelTab.click(); return true; }

                // Fallback: any role=tab whose text is exactly 'Reel'
                const banner = document.querySelector('[role=""banner""]');
                const tab = [...document.querySelectorAll('a[role=""tab""]')]
                    .find(t => !(banner?.contains(t)) && t.textContent.trim() === 'Reel');
                if (tab) { tab.click(); return true; }

                return false;
            }
        ");

        if (!tabClicked) return false;
        AppLogger.Info("-", "Facebook", "已點擊 Reel 分頁，等待內容載入...");
        await Task.Delay(2000, ct);

        // ── Step B: Click "建立 Reel" button ─────────────────────────────────
        var createClicked = await page.EvaluateAsync<bool>(@"
            () => {
                // Primary: aria-label='建立 Reel' + role='button' — exact match from DOM
                const btn = document.querySelector('[aria-label=""建立 Reel""][role=""button""]');
                if (btn) { btn.click(); return true; }

                // Fallback: text-based search outside banner
                const banner = document.querySelector('[role=""banner""]');
                const labels = ['建立 Reel', '建立Reel', 'Create reel', 'Create Reel'];
                for (const label of labels) {
                    const el = [...document.querySelectorAll('[role=""button""]')]
                        .find(b => !(banner?.contains(b)) && b.textContent.trim() === label);
                    if (el) { el.click(); return true; }
                }
                return false;
            }
        ");

        return createClicked;
    }

    // 觸發 Reel 建立介面中的影片上傳：依序嘗試文字按鈕、file input 直接觸發、aria-label 元素
    private static async Task<bool> TriggerVideoUploadAsync(IPage page)
    {
        return await page.EvaluateAsync<bool>(@"
            () => {
                // Try text-based buttons in the upload dialog
                const texts = ['選擇影片', 'Select video', '從電腦選擇', 'Select from computer',
                               '選擇', 'Add video', '新增影片', '上傳影片', 'Upload video'];
                const all = [...document.querySelectorAll('[role=""button""], button, span, div, label')];
                for (const text of texts) {
                    const el = all.find(e => e.childElementCount === 0 && e.textContent.trim() === text);
                    if (el) {
                        const btn = el.closest('[role=""button""], button, label') ?? el;
                        btn.click();
                        return true;
                    }
                }
                // Try file input directly
                const input = document.querySelector('input[type=""file""]');
                if (input) { input.click(); return true; }
                // Try aria-label containing video/upload keywords
                const uploadZone = document.querySelector(
                    '[role=""button""][aria-label*=""影片""], [role=""button""][aria-label*=""video""], ' +
                    '[role=""button""][aria-label*=""upload""], [role=""button""][aria-label*=""上傳""]'
                );
                if (uploadZone) { uploadZone.click(); return true; }
                return false;
            }
        ");
    }

    // 影片上傳後點擊「繼續」按鈕以進入下一個精靈步驟
    // 從 DOM 確認：role="form" aria-label="Reel" 表單內的按鈕，文字為「繼續」
    private static async Task<bool> ClickContinueButtonAsync(IPage page)
    {
        return await page.EvaluateAsync<bool>(@"
            () => {
                const labels = ['繼續', 'Continue'];
                // Search inside the Reel form first for precision
                const form = document.querySelector('[role=""form""][aria-label=""Reel""]');
                const scope = form ?? document;
                for (const label of labels) {
                    const btn = [...scope.querySelectorAll('[role=""button""], button')]
                        .find(b => b.textContent.trim() === label);
                    if (btn) { btn.click(); return true; }
                }
                // Fallback: aria-label
                const byLabel = document.querySelector('[aria-label=""繼續""], [aria-label=""Continue""]');
                if (byLabel) { byLabel.click(); return true; }
                return false;
            }
        ");
    }

    // 在精靈步驟中點擊「下一步」直到找到說明文字欄位（最多嘗試 4 次）
    private static async Task<IElementHandle?> NavigateToDetailsStepAsync(IPage page, CancellationToken ct)
    {
        // Check if caption field is immediately visible (single-step flow)
        var captionEl = await FindAnyAsync(page, FacebookSelectors.CaptionArea, timeoutMs: 2000);
        if (captionEl != null) return captionEl;

        for (int step = 0; step < 4; step++)
        {
            var nextClicked = await page.EvaluateAsync<bool>(@"
                () => {
                    const texts = ['下一步', 'Next'];
                    const all = [...document.querySelectorAll('[role=""button""], button')];
                    for (const text of texts) {
                        const btn = all.find(b => b.textContent.trim() === text);
                        if (btn) { btn.click(); return true; }
                    }
                    return false;
                }
            ");

            if (!nextClicked) break;
            await Task.Delay(1500, ct);

            captionEl = await FindAnyAsync(page, FacebookSelectors.CaptionArea, timeoutMs: 2000);
            if (captionEl != null) return captionEl;
        }

        return null;
    }

    // 透過剪貼簿貼入說明文字，使各種 contenteditable 編輯器都能正確接收輸入
    private async Task FillCaptionAsync(
        IPage page, IBrowserContext context, IElementHandle captionEl, string caption, CancellationToken ct)
    {
        var box = await captionEl.BoundingBoxAsync();
        if (box == null)
        {
            AppLogger.Warn("-", PlatformName, "說明欄位無法取得位置，略過");
            return;
        }

        float cx = (float)(box.X + box.Width / 2);
        float cy = (float)(box.Y + box.Height / 2);

        try
        {
            await context.GrantPermissionsAsync(
                new[] { "clipboard-read", "clipboard-write" },
                new BrowserContextGrantPermissionsOptions { Origin = _cfg.BaseUrl });
        }
        catch (Exception ex)
        {
            AppLogger.Warn("-", PlatformName, $"GrantPermissions 失敗: {ex.Message}");
        }

        await page.Mouse.MoveAsync(cx, cy);
        await Task.Delay(150, ct);
        await page.Mouse.ClickAsync(cx, cy);
        await Task.Delay(200, ct);

        var lines = caption.Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            if (lines[li].Length > 0)
            {
                await page.EvaluateAsync<bool>(@"
                    async (text) => {
                        try {
                            await navigator.clipboard.writeText(text);
                            return true;
                        } catch {
                            const ta = document.createElement('textarea');
                            ta.value = text;
                            ta.style.position = 'fixed';
                            ta.style.opacity = '0';
                            document.body.appendChild(ta);
                            ta.select();
                            const r = document.execCommand('copy');
                            document.body.removeChild(ta);
                            return r;
                        }
                    }
                ", lines[li]);
                await Task.Delay(100, ct);
                // Re-focus after clipboard ops steal focus
                await page.Mouse.ClickAsync(cx, cy);
                await Task.Delay(80, ct);
                await page.Keyboard.PressAsync("Control+v");
                await Task.Delay(150, ct);
            }
            if (li < lines.Length - 1)
            {
                await page.Keyboard.PressAsync("Enter");
                await Task.Delay(80, ct);
            }
        }

        AppLogger.Info("-", PlatformName, "已輸入說明文字");
    }

    // 點擊「分享」或「發佈」按鈕完成 Reel 發布
    // 從 DOM 確認：發佈按鈕為 <div aria-label="發佈" role="button">，內文 span 為「發佈」
    private static async Task<bool> ClickPublishButtonAsync(IPage page)
    {
        return await page.EvaluateAsync<bool>(@"
            () => {
                // Primary: aria-label='發佈' + role='button' — exact match from DOM
                const byLabel = document.querySelector('[aria-label=""發佈""][role=""button""]');
                if (byLabel) { byLabel.click(); return true; }

                // Fallback English
                const byLabelEn = document.querySelector('[aria-label=""Publish""][role=""button""], [aria-label=""Share""][role=""button""]');
                if (byLabelEn) { byLabelEn.click(); return true; }

                // Last resort: text match
                const texts = ['發佈', '分享', 'Publish', 'Share', 'Post'];
                for (const text of texts) {
                    const btn = [...document.querySelectorAll('[role=""button""], button')]
                        .find(b => b.textContent.trim() === text);
                    if (btn) { btn.click(); return true; }
                }
                return false;
            }
        ");
    }

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

    private static async Task SaveDebugScreenshot(IPage page, string tag)
    {
        try
        {
            Directory.CreateDirectory("debug");
            var path = $"debug/fb_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        }
        catch { }
    }

    private string BuildCaption(string description)
    {
        var tags = string.Join(" ", _cfg.DefaultCaptionHashtags);
        return string.IsNullOrWhiteSpace(description) ? tags : $"{description}\n\n{tags}";
    }

    private double RandomBetween(double min, double max) =>
        min + (max - min) * _rng.NextDouble();
}
