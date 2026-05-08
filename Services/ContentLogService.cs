using Newtonsoft.Json;
using SocialMaster.Models;

namespace SocialMaster.Services;

public class ContentLogService
{
    private readonly string _logPath;
    private ContentLog _log = new();
    private readonly Lock _lock = new();

    public ContentLogService(string accountDir)
    {
        _logPath = Path.Combine(accountDir, "content_log.json");
    }

    // 從 content_log.json 載入所有記錄；檔案不存在時建立空的記錄集
    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_logPath)) { _log = new(); return; }
            _log = JsonConvert.DeserializeObject<ContentLog>(
                File.ReadAllText(_logPath)) ?? new ContentLog();
        }
    }

    // 檢查指定 sourceId 是否已有下載記錄（不論是否已上傳）
    public bool IsDownloaded(string sourceId)
    {
        lock (_lock) return _log.Entries.Any(e => e.SourceId == sourceId);
    }

    // 檢查指定 sourceId 是否已成功上傳（發文）
    public bool IsUploaded(string sourceId)
    {
        lock (_lock) return _log.Entries.Any(e => e.SourceId == sourceId && e.Uploaded);
    }

    // 新增一筆下載完成記錄；若 sourceId 已存在則忽略（冪等操作）
    public void MarkDownloaded(string sourceId, string sourceType, string localPath, string description = "")
    {
        lock (_lock)
        {
            if (_log.Entries.Any(e => e.SourceId == sourceId)) return;
            _log.Entries.Add(new ContentLogEntry
            {
                SourceId = sourceId,
                SourceType = sourceType,
                Description = description,
                LocalPath = localPath,
                DownloadedAt = DateTime.Now,
                Uploaded = false,
            });
            Save();
        }
    }

    // 將指定記錄標記為已上傳，並記錄上傳時間
    public void MarkUploaded(string sourceId)
    {
        lock (_lock)
        {
            var entry = _log.Entries.FirstOrDefault(e => e.SourceId == sourceId);
            if (entry == null) return;
            entry.Uploaded = true;
            entry.UploadedAt = DateTime.Now;
            Save();
        }
    }

    // 遞增指定記錄的上傳嘗試次數（不論成功或失敗都會呼叫）
    public void MarkUploadAttempted(string sourceId)
    {
        lock (_lock)
        {
            var entry = _log.Entries.FirstOrDefault(e => e.SourceId == sourceId);
            if (entry == null) return;
            entry.UploadAttempts++;
            Save();
        }
    }

    // Scan the videos directory and register any .mp4 files not yet in the log.
    // Returns the number of entries added (0 = nothing new found).
    public int SyncFromDisk(string videosDir, string sourceType)
    {
        if (!Directory.Exists(videosDir)) return 0;
        lock (_lock)
        {
            int added = 0;
            foreach (var file in Directory.GetFiles(videosDir, "*.mp4"))
            {
                var sourceId = Path.GetFileNameWithoutExtension(file);
                if (_log.Entries.Any(e => e.SourceId == sourceId)) continue;

                _log.Entries.Add(new ContentLogEntry
                {
                    SourceId = sourceId,
                    SourceType = sourceType,
                    LocalPath = file,
                    DownloadedAt = File.GetCreationTime(file),
                    Uploaded = false,
                });
                added++;
            }
            if (added > 0) Save();
            return added;
        }
    }

    // Returns the oldest downloaded-but-not-uploaded entry whose file still exists on disk
    public ContentLogEntry? GetPendingUpload()
    {
        lock (_lock)
        {
            return _log.Entries
                .Where(e => !e.Uploaded && File.Exists(e.LocalPath))
                .OrderBy(e => e.DownloadedAt)
                .FirstOrDefault();
        }
    }

    // 刪除所有 uploaded=true 記錄對應的本地影片檔案，並清空 LocalPath；回傳刪除數量
    public int CleanupUploadedFiles()
    {
        lock (_lock)
        {
            int deleted = 0;
            foreach (var entry in _log.Entries.Where(e => e.Uploaded && !string.IsNullOrEmpty(e.LocalPath)))
            {
                if (!File.Exists(entry.LocalPath)) { entry.LocalPath = ""; continue; }
                try
                {
                    File.Delete(entry.LocalPath);
                    entry.LocalPath = "";
                    deleted++;
                }
                catch { }
            }
            if (deleted > 0) Save();
            return deleted;
        }
    }

    public int TotalDownloaded
    {
        get { lock (_lock) return _log.Entries.Count; }
    }

    public int TotalUploaded
    {
        get { lock (_lock) return _log.Entries.Count(e => e.Uploaded); }
    }

    // 將目前記錄集序列化並寫入 content_log.json（應在持有 _lock 時呼叫）
    private void Save()
    {
        File.WriteAllText(_logPath, JsonConvert.SerializeObject(_log, Formatting.Indented));
    }
}
