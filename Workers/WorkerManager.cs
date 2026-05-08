using SocialMaster.Factories;
using SocialMaster.Helpers;
using SocialMaster.Models;
using SocialMaster.Services;

namespace SocialMaster.Workers;

public class WorkerManager
{
    private readonly Dictionary<int, AccountWorker> _workers = new();
    private readonly SocialConfig _config;

    public event EventHandler<WorkerStateChangedEventArgs>? WorkerStateChanged;

    public WorkerManager(SocialConfig config)
    {
        _config = config;
    }

    // 為每個已啟用的帳號建立 AccountWorker；已存在的帳號 ID 會被略過（冪等操作）
    public void BuildWorkers(IEnumerable<AccountProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            if (_workers.ContainsKey(profile.Id)) continue;
            if (!profile.IsEnabled) continue;

            try
            {
                var contentLog = new ContentLogService(profile.AccountDir);
                contentLog.Load();
                var synced = contentLog.SyncFromDisk(profile.VideosDir, profile.SourceType);
                if (synced > 0)
                    AppLogger.Warn(profile.Id.ToString(), profile.Platform,
                        $"SyncFromDisk 從磁碟補回 {synced} 筆影片記錄（description 為空）。若要重新抓 API，請同時刪除 videos 目錄內的 .mp4 檔案再重啟。");
                var cleaned = contentLog.CleanupUploadedFiles();
                if (cleaned > 0)
                    AppLogger.Info(profile.Id.ToString(), profile.Platform,
                        $"清除 {cleaned} 筆已上傳的本地影片檔案");

                var platform = PlatformFactory.Create(profile.Platform, _config, profile);
                var source = SourceFactory.Create(
                    profile.SourceType, contentLog, profile.VideosDir, _config);

                var platformCfg = _config.Platforms.TryGetValue(profile.Platform, out var cfg)
                    ? cfg : new PlatformConfig();

                var worker = new AccountWorker(profile, platform, source, platformCfg, contentLog);
                worker.StateChanged += (s, e) => WorkerStateChanged?.Invoke(s, e);
                _workers[profile.Id] = worker;
            }
            catch (Exception ex)
            {
                AppLogger.Error(profile.Id.ToString(), profile.Platform, "建立 Worker 失敗", ex);
            }
        }
    }

    // 啟動所有 worker；可指定平台名稱篩選，傳 null 則啟動全部
    public void StartAll(string? platformFilter = null)
    {
        foreach (var w in GetFiltered(platformFilter)) w.Start();
    }

    // 停止所有 worker；可指定平台名稱篩選，傳 null 則停止全部
    public void StopAll(string? platformFilter = null)
    {
        foreach (var w in GetFiltered(platformFilter)) w.Stop();
    }

    // 啟動指定帳號 ID 的 worker
    public void Start(int accountId)
    {
        if (_workers.TryGetValue(accountId, out var w)) w.Start();
    }

    // 停止指定帳號 ID 的 worker
    public void Stop(int accountId)
    {
        if (_workers.TryGetValue(accountId, out var w)) w.Stop();
    }

    // 取得指定帳號 ID 的當前狀態；找不到時回傳 Stopped
    public WorkerState GetState(int accountId) =>
        _workers.TryGetValue(accountId, out var w) ? w.CurrentState : WorkerState.Stopped;

    // 取得指定帳號 ID 的下次發文時間；找不到時回傳 null
    public DateTime? GetNextActionTime(int accountId) =>
        _workers.TryGetValue(accountId, out var w) ? w.NextActionTime : null;

    // 所有已建立的 worker 集合
    public IEnumerable<AccountWorker> AllWorkers => _workers.Values;

    // 依平台名稱篩選 worker；platform 為 null 時回傳全部
    private IEnumerable<AccountWorker> GetFiltered(string? platform) =>
        platform == null
            ? _workers.Values
            : _workers.Values.Where(w => w.Platform == platform);
}
