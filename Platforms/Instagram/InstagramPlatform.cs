using Microsoft.Playwright;
using SocialMaster.Abstractions;
using SocialMaster.Helpers;
using SocialMaster.Models;

namespace SocialMaster.Platforms.Instagram;

public class InstagramPlatform : ISocialPlatform
{
    public string PlatformName => "Instagram";

    private readonly PlatformConfig _cfg;
    private readonly Random _rng = new();

    public InstagramPlatform(PlatformConfig config)
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
            return await FindAnyAsync(page, InstagramSelectors.NavHomeIcon) != null;
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

        AppLogger.Info("-", PlatformName, "瀏覽器已開啟，請手動登入 Instagram...");

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(3000, ct);
            try
            {
                if (await FindAnyAsync(page, InstagramSelectors.NavHomeIcon) != null)
                {
                    AppLogger.Info("-", PlatformName, "偵測到登入成功，繼續執行");
                    return;
                }
            }
            catch { /* page may be navigating, retry */ }
        }
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    // 執行完整的 Instagram Reels 上傳流程：關閉彈窗 → 點擊建立 → 選檔 → 裁切為原始比例 → 輸入說明文字（透過剪貼簿 Ctrl+V 寫入 Lexical 編輯器）→ 點擊分享
    public async Task<bool> UploadContentAsync(IBrowserContext context, ContentItem item, CancellationToken ct)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        try
        {
            await page.GotoAsync(_cfg.BaseUrl, new PageGotoOptions
            {
                Timeout = _cfg.NavigationTimeoutSeconds * 1000,
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            // Dismiss any popup dialogs before interacting with the page
            await DismissPopupsAsync(page);

            // Step 1: click the create button
            if (!await ClickCreateButtonAsync(page, ct))
            {
                await SaveDebugScreenshot(page, "no_create_btn");
                AppLogger.Error("-", PlatformName, "找不到新增貼文按鈕，已截圖至 debug/ 資料夾");
                return false;
            }

            await Task.Delay(1500, ct);

            // Step 2: if a Reel/Post menu appeared, pick Reel via JS click (overlay blocks ClickAsync)
            var reelItem = await FindAnyAsync(page, InstagramSelectors.ReelMenuItem);
            if (reelItem != null)
            {
                await reelItem.EvaluateAsync("el => el.click()");
                await Task.Delay(1000, ct);
            }

            // Step 3: scope inside the "建立新貼文" dialog, find button._aswp, JS-click it.
            // An overlay div intercepts pointer events so ClickAsync always times out.
            // WaitForFileChooserAsync intercepts the OS file dialog before it appears.
            await page.WaitForSelectorAsync(
                "[aria-label='建立新貼文'][role='dialog']",
                new PageWaitForSelectorOptions { Timeout = 10000, State = WaitForSelectorState.Visible });

            var fileChooserTask = page.WaitForFileChooserAsync(
                new PageWaitForFileChooserOptions { Timeout = 15000 });

            var btnClicked = await page.EvaluateAsync<bool>(@"
                () => {
                    const dialog = document.querySelector('[aria-label=""建立新貼文""][role=""dialog""]');
                    if (!dialog) return false;
                    const btn = dialog.querySelector('button._aswp');
                    if (!btn) return false;
                    btn.click();
                    return true;
                }
            ");

            if (!btnClicked)
            {
                await SaveDebugScreenshot(page, "no_select_btn");
                AppLogger.Error("-", PlatformName, "dialog 內找不到「從電腦選擇」按鈕，已截圖");
                return false;
            }

            var fileChooser = await fileChooserTask;
            await fileChooser.SetFilesAsync(item.FilePath);
            AppLogger.Info("-", PlatformName, "已選擇影片檔案，等待處理...");

            // Step 4: dismiss popups that may appear after file selection
            // First try the "影片貼文現在會以 Reel 形式分享" info popup (IG2 popup),
            // then fall back to generic confirm buttons for format/duration warnings.
            await Task.Delay(2000, ct);
            if (await DismissReelInfoPopupAsync(page))
            {
                AppLogger.Info("-", PlatformName, "已關閉 Reel 說明彈窗（影片貼文現在會以 Reel 形式分享）");
                await Task.Delay(1000, ct);
            }
            else
            {
                //var confirmBtn = await FindAnyAsync(page, InstagramSelectors.ConfirmPopupButtons, timeoutMs: 3000);
                //if (confirmBtn != null)
                //{
                //    await confirmBtn.EvaluateAsync("el => el.click()");
                //    AppLogger.Info("-", PlatformName, "已關閉確認彈窗");
                //    await Task.Delay(1000, ct);
                //}
            }

            // Step 5: crop screen — DOM traversal from div[role="presentation"] anchor
            //   anchor.previousSibling     = ratio/size button  → click to expand panel
            //   ratioBtn.previousSibling   = ratio options panel → find "原始" inside
            var cropHeading = await FindAnyAsync(page, InstagramSelectors.CropScreenHeading, timeoutMs: 5000);
            if (cropHeading != null)
            {
                AppLogger.Info("-", PlatformName, "裁切畫面出現，點擊尺寸按鈕");

                // Wait for the crop screen to fully render
                await Task.Delay(1500, ct);

                // Click the crop/ratio button (svg aria-label="選擇「裁切」", class _aswq)
                var ratioBtn = await FindAnyAsync(page, InstagramSelectors.RatioSelectorButton, timeoutMs: 5000);
                if (ratioBtn != null)
                {
                    await page.EvaluateAsync(@"
                        () => document.querySelector('svg[aria-label=""選擇「裁切」""]').closest('button').click()
                    ");
                    AppLogger.Info("-", PlatformName, "已點擊裁切按鈕，等待比例面板展開");
                    await Task.Delay(1500, ct);

                    // Full-page scan for "原始" text
                    var originalSelected = await page.EvaluateAsync<bool>(@"
                        () => {
                            const all = document.querySelectorAll('*');
                            for (const el of all) {
                                if (el.childElementCount === 0 && el.textContent.trim() === '原始') {
                                    el.click();
                                    return true;
                                }
                            }
                            return false;
                        }
                    ");

                    AppLogger.Info("-", PlatformName, originalSelected ? "已選擇原始比例" : "找不到「原始」選項，繼續流程");
                    await Task.Delay(600, ct);
                }
                else
                {
                    await SaveDebugScreenshot(page, "no_ratio_btn");
                    AppLogger.Warn("-", PlatformName, "找不到裁切按鈕，已截圖至 debug/");
                }
            }

            // Step 6: click Next inside the "裁切" dialog
            await Task.Delay(2000, ct);
            await page.EvaluateAsync(@"
                () => {
                    const btn = [...document.querySelector('[aria-label=""裁切""][role=""dialog""]')?.querySelectorAll('[role=""button""]')??[]].find(el=>el.textContent.trim()==='下一步');
                    btn?.dispatchEvent(new MouseEvent('click', {bubbles: true, cancelable: true}));
                    const btnEn = [...document.querySelector('[aria-label=""Crop""][role=""dialog""]')?.querySelectorAll('[role=""button""]')??[]].find(el=>el.textContent.trim()==='Next');
                    btnEn?.dispatchEvent(new MouseEvent('click', {bubbles: true, cancelable: true}));
                }
            ");
            AppLogger.Info("-", PlatformName, "已點擊裁切畫面的下一步");

            // Step 7: click Next inside the "編輯" dialog
            await Task.Delay(2000, ct);
            await page.EvaluateAsync(@"
                () => {
                    const btn = [...document.querySelector('[aria-label=""編輯""][role=""dialog""]')?.querySelectorAll('[role=""button""]')??[]].find(el=>el.textContent.trim()==='下一步');
                    btn?.dispatchEvent(new MouseEvent('click', {bubbles: true, cancelable: true}));
                    const btnEn = [...document.querySelector('[aria-label=""Edit""][role=""dialog""]')?.querySelectorAll('[role=""button""]')??[]].find(el=>el.textContent.trim()==='Next');
                    btnEn?.dispatchEvent(new MouseEvent('click', {bubbles: true, cancelable: true}));
                }
            ");
            AppLogger.Info("-", PlatformName, "已點擊編輯畫面的下一步");

            await Task.Delay(2000, ct);

            // Step 8: fill caption via real clipboard + Ctrl+V
            // Why: Lexical's EditorState only updates from real paste/beforeinput events.
            // Playwright TypeAsync inserts into DOM, but Lexical's state listener doesn't always
            // fire reliably. Real Ctrl+V triggers a true browser paste event (isTrusted=true) →
            // Lexical's paste handler correctly updates EditorState → React state → API payload.
            var caption = BuildCaption(item.Description);
            AppLogger.Info("-", PlatformName, $"Caption ({caption.Length}字): {(caption.Length > 60 ? caption[..60] + "…" : caption)}");

            var captionEl = await FindAnyAsync(page, InstagramSelectors.CaptionArea, timeoutMs: 8000);
            if (captionEl != null)
            {
                // Step 8a: grant clipboard permission so navigator.clipboard.writeText works
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

                // Step 8b: move mouse + left-click to focus the Lexical editor
                var editorHandle = await page.QuerySelectorAsync("[data-lexical-editor='true']");
                bool focused = false;
                float editorCx = 0, editorCy = 0;
                if (editorHandle != null)
                {
                    var box = await editorHandle.BoundingBoxAsync();
                    if (box != null)
                    {
                        editorCx = box.X + box.Width / 2;
                        editorCy = box.Y + box.Height / 2;
                        await page.Mouse.MoveAsync(editorCx, editorCy);
                        await Task.Delay(150, ct);
                        await page.Mouse.ClickAsync(editorCx, editorCy);
                        await Task.Delay(300, ct);
                        focused = true;
                    }
                }

                // Step 8c: paste line-by-line — paste text, press Enter for paragraph breaks
                if (focused)
                {
                    var lines = caption.Split('\n');
                    for (int li = 0; li < lines.Length; li++)
                    {
                        if (lines[li].Length > 0)
                        {
                            var ok = await page.EvaluateAsync<bool>(@"
                                async (text) => {
                                    try {
                                        await navigator.clipboard.writeText(text);
                                        return true;
                                    } catch (e) {
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
                            if (!ok)
                                AppLogger.Warn("-", PlatformName, $"Clipboard 寫入失敗 line {li}");
                            await Task.Delay(120, ct);
                            // Re-focus the editor center — execCommand fallback steals focus via temp textarea
                            await page.Mouse.ClickAsync(editorCx, editorCy);
                            await Task.Delay(100, ct);
                            await page.Keyboard.PressAsync("Control+v");
                            await Task.Delay(200, ct);
                        }
                        if (li < lines.Length - 1)
                        {
                            await page.Keyboard.PressAsync("Enter");
                            await Task.Delay(80, ct);
                        }
                    }
                }

                await Task.Delay(1000, ct);
                // Verify both DOM textContent AND Lexical's EditorState — they should match
                var diag = await page.EvaluateAsync<string>(@"
                    () => {
                        const el = document.querySelector('[data-lexical-editor=""true""]');
                        if (!el) return 'no-editor';
                        const dom = el.textContent || '';
                        let stateText = '';
                        try {
                            const ed = el.__lexicalEditor;
                            if (ed) {
                                const json = ed.getEditorState().toJSON();
                                const collect = n => {
                                    if (n.text) return n.text;
                                    if (n.children) return n.children.map(collect).join('');
                                    return '';
                                };
                                stateText = collect(json.root);
                            }
                        } catch(e) {}
                        return 'DOM=[' + dom.slice(0,40) + '] STATE=[' + stateText.slice(0,40) + ']';
                    }
                ");
                AppLogger.Info("-", PlatformName, $"Caption {(focused ? "已輸入" : "失敗")}，{diag}");
            }
            else
            {
                AppLogger.Warn("-", PlatformName, "找不到說明文字欄位，略過");
                await SaveDebugScreenshot(page, "no_caption_el");
            }

            // Blur the caption editor before sharing so Instagram's onBlur handler
            // commits Lexical EditorState into the React caption state before submission.
            //await page.Mouse.MoveAsync(10, 10);
            //await page.Mouse.ClickAsync(10, 10);
            //await Task.Delay(500, ct);

            await Task.Delay(1500, ct);

            // Step 9: share — use Playwright Locator with text filter, then ClickAsync
            var shareLocator = page.Locator("[role='dialog'] [role='button']").Filter(new LocatorFilterOptions { HasText = "分享" });
            if (await shareLocator.CountAsync() == 0)
                shareLocator = page.Locator("[role='dialog'] [role='button']").Filter(new LocatorFilterOptions { HasText = "Share" });

            if (await shareLocator.CountAsync() > 0)
            {
                await shareLocator.First.ScrollIntoViewIfNeededAsync();
                await shareLocator.First.ClickAsync();
                AppLogger.Info("-", PlatformName, "已點擊分享按鈕");
            }
            else
            {
                AppLogger.Warn("-", PlatformName, "找不到分享按鈕");
            }

            // Step 10: wait for success
            var success = await FindAnyAsync(page, InstagramSelectors.UploadSuccess,
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
            await page.GotoAsync(_cfg.BaseUrl, new PageGotoOptions
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
        // Try each dismiss button; keep trying until none are found
        for (int pass = 0; pass < 3; pass++)
        {
            var btn = await FindAnyAsync(page, InstagramSelectors.PopupDismissButtons, timeoutMs: 2000);
            if (btn == null) break;
            try
            {
                await btn.ClickAsync();
                await Task.Delay(800);
            }
            catch { break; }
        }
    }

    // 偵測「影片貼文現在會以 Reel 形式分享」彈窗：搜尋帶有 aria-modal 的 dialog，
    // 確認 h2 標題含有 "Reel" 或 "影片貼文" 後，點擊內部的「確定」/「OK」按鈕
    private static async Task<bool> DismissReelInfoPopupAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<bool>(@"
                () => {
                    const dialogs = document.querySelectorAll('[aria-modal=""true""][role=""dialog""]');
                    for (const dialog of dialogs) {
                        const h2 = dialog.querySelector('h2');
                        if (!h2) continue;
                        const title = h2.textContent || '';
                        if (!title.includes('Reel') && !title.includes('影片貼文')) continue;
                        const btn = [...dialog.querySelectorAll('button')].find(
                            b => b.textContent.trim() === '確定' || b.textContent.trim() === 'OK');
                        if (btn) { btn.click(); return true; }
                    }
                    return false;
                }
            ");
        }
        catch { return false; }
    }

    // 點擊側邊欄的「建立貼文」按鈕，依序嘗試 aria-label selector 與文字內容 fallback
    private async Task<bool> ClickCreateButtonAsync(IPage page, CancellationToken ct)
    {
        // Hover the left sidebar first so labels and aria-labels appear
        try
        {
            await page.HoverAsync("nav, [role='navigation'], header", new PageHoverOptions { Timeout = 3000 });
            await Task.Delay(600, ct);
        }
        catch { /* sidebar may not need hover */ }

        // Try aria-label selectors
        foreach (var selector in InstagramSelectors.CreateButton)
        {
            try
            {
                var el = await page.QuerySelectorAsync(selector);
                if (el == null) continue;
                await el.EvaluateAsync(
                    "el => (el.closest('a, button, div[role=\"button\"]') ?? el).click()");
                AppLogger.Info("-", PlatformName, $"已點擊建立按鈕 ({selector})");
                return true;
            }
            catch { }
        }

        // Fallback: find by visible text content via JavaScript
        var texts = new[] { "新增", "建立", "Create", "New post" };
        foreach (var text in texts)
        {
            try
            {
                var clicked = await page.EvaluateAsync<bool>($@"
                    () => {{
                        const all = [...document.querySelectorAll('a, [role=""link""], [role=""button""], span, div')];
                        const el = all.find(e => e.childElementCount === 0 && e.textContent.trim() === '{text}');
                        if (!el) return false;
                        const target = el.closest('a, button, [role=""button""]') ?? el;
                        target.click();
                        return true;
                    }}");
                if (clicked)
                {
                    AppLogger.Info("-", PlatformName, $"已透過文字「{text}」點擊建立按鈕");
                    return true;
                }
            }
            catch { }
        }

        return false;
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
            var path = $"debug/ig_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        }
        catch { /* screenshot is best-effort */ }
    }

    // 將影片說明文字與設定的 hashtag 組合成完整說明文字
    private string BuildCaption(string description)
    {
        var tags = string.Join(" ", _cfg.DefaultCaptionHashtags);
        return string.IsNullOrWhiteSpace(description) ? tags : $"{description}\n\n{tags}";
    }

    // 在 min 與 max 之間產生均勻分布的隨機浮點數
    private double RandomBetween(double min, double max) =>
        min + (max - min) * _rng.NextDouble();

}
