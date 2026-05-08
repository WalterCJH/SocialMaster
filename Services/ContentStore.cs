namespace SocialMaster.Services;

public class ContentStore
{
    private readonly string _downloadedFile;
    private readonly HashSet<string> _downloaded = new();
    private readonly Lock _lock = new();

    public ContentStore(string downloadedFile)
    {
        _downloadedFile = downloadedFile;
    }

    // 從純文字檔案載入已下載 ID 清單，每行一筆
    public void Load()
    {
        _downloaded.Clear();
        if (!File.Exists(_downloadedFile)) return;
        foreach (var line in File.ReadAllLines(_downloadedFile))
        {
            var id = line.Trim();
            if (!string.IsNullOrEmpty(id))
                _downloaded.Add(id);
        }
    }

    // 檢查指定 sourceId 是否已在已下載清單中
    public bool IsDownloaded(string sourceId)
    {
        lock (_lock) return _downloaded.Contains(sourceId);
    }

    // 新增 sourceId 至已下載清單，並以 append 方式寫入檔案（避免重寫整個檔案）
    public void MarkDownloaded(string sourceId)
    {
        lock (_lock)
        {
            if (_downloaded.Add(sourceId))
                File.AppendAllText(_downloadedFile, sourceId + "\n");
        }
    }

    // 已下載的 ID 總數
    public int Count
    {
        get { lock (_lock) return _downloaded.Count; }
    }
}
