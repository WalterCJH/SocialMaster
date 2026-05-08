using Microsoft.Playwright;
using SocialMaster.Abstractions;
using SocialMaster.Helpers;
using SocialMaster.Models;
using SocialMaster.Services;

namespace SocialMaster.Workers;

public class AccountWorker
{
    private readonly AccountProfile _profile;
    private readonly ISocialPlatform _platform;
    private readonly IContentSource _source;
    private readonly PlatformConfig _platformCfg;
    private readonly ContentLogService _contentLog;

    private CancellationTokenSource? _cts;
    private Task? _task;
    private readonly Random _rng = new();

    public event EventHandler<WorkerStateChangedEventArgs>? StateChanged;

    public WorkerState CurrentState { get; private set; } = WorkerState.Stopped;
    public DateTime? NextActionTime { get; private set; }
    public int AccountId => _profile.Id;
    public string Platform => _profile.Platform;

    public AccountWorker(
        AccountProfile profile,
        ISocialPlatform platform,
        IContentSource source,
        PlatformConfig platformCfg,
        ContentLogService contentLog)
    {
        _profile = profile;
        _platform = platform;
        _source = source;
        _platformCfg = platformCfg;
        _contentLog = contentLog;
    }

    // 啟動 worker 背景工作；若工作已在執行中則直接回傳
    public void Start()
    {
        if (_task is { IsCompleted: false }) return;
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    // 透過取消 CancellationToken 通知 worker 停止
    public void Stop() => _cts?.Cancel();

    // 判斷 worker 背景工作是否仍在執行中
    public bool IsRunning => _task is { IsCompleted: false };

    // worker 主迴圈：建立瀏覽器 → 確認登入 → 下載新影片 → 上傳 → 模擬瀏覽 → 等待下次發文
    private async Task RunAsync(CancellationToken ct)
    {
        SetState(WorkerState.Idle, "Worker 啟動");
        IBrowserContext? context = null;

        try
        {
            context = await CreateBrowserContextAsync();
            if (context == null)
            {
                SetState(WorkerState.Error, "無法建立瀏覽器");
                return;
            }

            if (!await _platform.IsLoggedInAsync(context))
            {
                SetState(WorkerState.WaitingForLogin, "尚未登入，請在瀏覽器中完成登入...");
                await _platform.WaitForLoginAsync(context, ct);
                if (ct.IsCancellationRequested) return;
                SetState(WorkerState.Idle, "登入完成，開始執行");
            }

            while (!ct.IsCancellationRequested)
            {
                // Priority: retry pending (downloaded but not yet uploaded) before fetching new
                var pending = _contentLog.GetPendingUpload();
                ContentItem? item;

                if (pending != null)
                {
                    AppLogger.Info(_profile.Id.ToString(), _profile.Platform,
                        $"發現未上傳的影片，重新嘗試: {pending.SourceId}");
                    item = new ContentItem
                    {
                        FilePath = pending.LocalPath,
                        Description = pending.Description,
                        SourceId = pending.SourceId,
                        SourceType = pending.SourceType,
                    };
                }
                else
                {
                    SetState(WorkerState.Downloading, "正在取得新內容...");
                    item = null;
                    try
                    {
                        item = await _source.GetNextContentAsync(_profile.SourceConfig, ct);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error(_profile.Id.ToString(), _profile.Platform, "取得內容失敗", ex);
                    }
                }

                if (item == null)
                {
                    SetState(WorkerState.Waiting, "無新內容，30 分鐘後重試");
                    NextActionTime = DateTime.Now.AddMinutes(30);
                    await Task.Delay(TimeSpan.FromMinutes(30), ct);
                    continue;
                }

                SetState(WorkerState.Uploading, $"正在上傳: {Truncate(item.Description, 20)}");
                _contentLog.MarkUploadAttempted(item.SourceId);

                bool uploaded = false;
                try
                {
                    uploaded = await _platform.UploadContentAsync(context, item, ct);
                }
                catch (Exception ex)
                {
                    AppLogger.Error(_profile.Id.ToString(), _profile.Platform, "上傳失敗", ex);
                }

                if (uploaded)
                {
                    _contentLog.MarkUploaded(item.SourceId);
                    if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                    {
                        try { File.Delete(item.FilePath); }
                        catch (Exception ex)
                        {
                            AppLogger.Warn(_profile.Id.ToString(), _profile.Platform,
                                $"刪除本地影片失敗: {ex.Message}");
                        }
                    }
                    AppLogger.Info(_profile.Id.ToString(), _profile.Platform,
                        $"成功發布並刪除本地影片: {item.SourceId}");
                }
                else
                {
                    AppLogger.Warn(_profile.Id.ToString(), _profile.Platform,
                        $"上傳失敗，下次啟動將重試: {item.SourceId}");
                }

                SetState(WorkerState.Nurturing, "模擬人類行為中...");
                try
                {
                    await _platform.NurtureAsync(context, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLogger.Warn(_profile.Id.ToString(), _profile.Platform, $"Nurture 異常: {ex.Message}");
                }

                var delay = GetRandomDelay();
                NextActionTime = DateTime.Now.Add(delay);
                SetState(WorkerState.Waiting, $"等待下次發文，預計 {NextActionTime:MM/dd HH:mm}");

                // Navigate existing page back to home so browser stays on the platform during wait
                var idlePage = context.Pages.FirstOrDefault();
                if (idlePage != null)
                {
                    try
                    {
                        await idlePage.GotoAsync(_platformCfg.BaseUrl, new PageGotoOptions
                        {
                            Timeout = 30000,
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                        });
                    }
                    catch { }
                }

                await Task.Delay(delay, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error(_profile.Id.ToString(), _profile.Platform, "Worker 意外錯誤", ex);
            SetState(WorkerState.Error, ex.Message);
            return;
        }
        finally
        {
            if (context != null) await context.CloseAsync();
        }

        SetState(WorkerState.Stopped, "Worker 已停止");
    }

    // 使用持久化設定目錄啟動 Chromium，並注入腳本隱藏 webdriver 標記以避免被平台偵測
    private async Task<IBrowserContext?> CreateBrowserContextAsync()
    {
        try
        {
            // AppLogger.Info(_profile.Id.ToString(), _profile.Platform, $"PLAYWRIGHT_BROWSERS_PATH：{Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH")}");
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", Path.Combine(AppContext.BaseDirectory, "ms-playwright"));
            var playwright = await Playwright.CreateAsync();
            var context = await playwright.Chromium.LaunchPersistentContextAsync(
                _profile.BrowserDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--no-first-run",
                        "--no-default-browser-check",
                    },
                });

            AppLogger.Info(_profile.Id.ToString(), _profile.Platform, $"Chromium Executable Path: {playwright.Chromium.ExecutablePath}");
            
            // Hide navigator.webdriver so sites like X don't detect automation
            await context.AddInitScriptAsync(
                "Object.defineProperty(navigator, 'webdriver', { get: () => undefined })");

            return context;
        }
        catch (Exception ex)
        {
            AppLogger.Error(_profile.Id.ToString(), _profile.Platform, "建立瀏覽器失敗", ex);
            return null;
        }
    }

    // 計算下次發文的隨機等待時間；帳號層級設定（分鐘）優先於平台預設設定（小時）
    private TimeSpan GetRandomDelay()
    {
        // Per-account interval (minutes) takes priority over platform config (hours)
        if (_profile.MinIntervalMinutes > 0 && _profile.MaxIntervalMinutes > _profile.MinIntervalMinutes)
        {
            var minM = _profile.MinIntervalMinutes;
            var maxM = _profile.MaxIntervalMinutes;
            return TimeSpan.FromMinutes(minM + (maxM - minM) * _rng.NextDouble());
        }
        var min = _platformCfg.DefaultMinIntervalHours;
        var max = _platformCfg.DefaultMaxIntervalHours;
        return TimeSpan.FromHours(min + (max - min) * _rng.NextDouble());
    }

    // 更新 CurrentState 並記錄日誌，同時觸發 StateChanged 事件通知 UI 更新
    private void SetState(WorkerState state, string message)
    {
        CurrentState = state;
        AppLogger.Info(_profile.Id.ToString(), _profile.Platform, message);
        StateChanged?.Invoke(this, new WorkerStateChangedEventArgs
        {
            AccountId = _profile.Id,
            Platform = _profile.Platform,
            State = state,
            Message = message,
            NextActionTime = NextActionTime,
        });
    }

    // 截斷字串至指定長度，超出時以 "..." 結尾
    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
