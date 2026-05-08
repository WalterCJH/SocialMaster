using Newtonsoft.Json;
using SocialMaster.Abstractions;
using SocialMaster.Helpers;
using SocialMaster.Services;
using System.Text.RegularExpressions;

namespace SocialMaster.Sources.Douyin;

public class DouyinSource : IContentSource
{
    public string SourceName => "Douyin";

    private readonly ContentLogService _contentLog;
    private readonly string _videosDir;
    private readonly DouyinConfig _cfg;
    private readonly HttpClient _http;

    private readonly string _jsonDir;
    private readonly string _apiResponseDir;
    private readonly string _accountId;

    public DouyinSource(ContentLogService contentLog, string videosDir, DouyinConfig config)
    {
        _contentLog = contentLog;
        _videosDir = videosDir;
        _jsonDir = Path.Combine(Path.GetDirectoryName(videosDir)!, "json");
        _accountId = Path.GetFileName(Path.GetDirectoryName(videosDir)!) ?? "unknown";
        _apiResponseDir = Path.Combine("API_Response", "Douyin");
        _cfg = config;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", _cfg.AuthToken);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    // 從帳號設定的 URL 解析 sec_user_id，呼叫 API 並立即批次下載所有未上傳影片
    public async Task<ContentItem?> GetNextContentAsync(string sourceConfig, CancellationToken ct)
    {
        var secUserId = ExtractSecUserId(sourceConfig);
        if (secUserId == null)
        {
            AppLogger.Warn("-", SourceName, $"無法從 URL 提取 sec_user_id: {sourceConfig}");
            return null;
        }
        return await FetchAndDownloadAllAsync(secUserId, ct);
    }

    // 使用多種正規表達式模式從抖音 URL 中提取 sec_user_id，失敗時回傳 null
    private static string? ExtractSecUserId(string url)
    {
        var patterns = new[]
        {
            @"sec_user_id=([^&\s]+)",
            @"/user/([^/?&\s]+)",
            @"user_id=([^&\s]+)",
        };
        foreach (var pattern in patterns)
        {
            var m = Regex.Match(url, pattern);
            if (m.Success)
                return Regex.Replace(m.Groups[1].Value, @"[\x00-\x1f\x7f-\x9f\s]", "");
        }
        return null;
    }

    // 組合 TikHub API 的請求 URL，包含 sec_user_id、分頁游標與每頁筆數
    private string BuildFetchUrl(string secUserId, long maxCursor) =>
        $"https://{_cfg.ApiHost}/api/v1/douyin/app/v3/fetch_user_post_videos" +
        $"?sec_user_id={Uri.EscapeDataString(secUserId)}&max_cursor={maxCursor}&count={_cfg.PageSize}";

    // 呼叫 TikHub API 取得指定頁的影片清單；請求失敗或網路錯誤時回傳 null
    private async Task<DouyinApiResponse?> FetchUserVideosAsync(
        string secUserId, long maxCursor, CancellationToken ct)
    {
        try
        {
            var url = BuildFetchUrl(secUserId, maxCursor);
            var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<DouyinApiResponse>(json);
        }
        catch (Exception ex)
        {
            AppLogger.Error("-", SourceName, "API 請求失敗", ex);
            return null;
        }
    }

    // 排除圖片集（media_type=68）及無有效播放位址的影片，確保只處理真正的影片
    private static bool IsVideoWithVisualContent(DouyinAweme video)
    {
        if (video.MediaType == 68) return false;
        var v = video.Video;
        if (v == null || v.Width == 0 || v.Height == 0) return false;
        return v.PlayAddrH264?.UrlList.Count > 0
            || v.PlayAddr?.UrlList.Count > 0
            || v.PlayAddr265?.UrlList.Count > 0;
    }

    // 依序嘗試 H264、通用、H265 播放位址，優先取無 logo_type 浮水印的 URL
    private static string? GetNoWatermarkUrl(DouyinVideo video)
    {
        foreach (var addr in new[] { video.PlayAddrH264, video.PlayAddr, video.PlayAddr265 })
        {
            var url = addr?.UrlList.FirstOrDefault();
            if (!string.IsNullOrEmpty(url) && !url.Contains("logo_type"))
                return url;
        }
        return video.DownloadAddr?.UrlList.FirstOrDefault();
    }

    // 將整頁 API 回應連同取得時間與 URL 存至 API_Response/Douyin/{sec_user_id}/
    private void SaveApiResponseJson(string secUserId, string apiUrl, int page, DouyinApiResponse response)
    {
        try
        {
            var dir = Path.Combine(_apiResponseDir, secUserId);
            Directory.CreateDirectory(dir);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{_accountId}_{timestamp}_p{page}.json";
            var wrapper = new
            {
                fetched_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                api_url = apiUrl,
                data = response,
            };
            File.WriteAllText(Path.Combine(dir, fileName),
                JsonConvert.SerializeObject(wrapper, Formatting.Indented));
            AppLogger.Info("-", SourceName, $"已儲存 API 回應: {fileName}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("-", SourceName, $"儲存 API 回應失敗: {ex.Message}");
        }
    }

    // 將影片原始資料連同取得時間與 URL 存至 accounts/{ID}/json/{awemeId}.json（供查閱用）
    private void SaveAwemeJson(string awemeId, DouyinAweme aweme, string apiUrl)
    {
        try
        {
            Directory.CreateDirectory(_jsonDir);
            var path = Path.Combine(_jsonDir, $"{awemeId}.json");
            var wrapper = new
            {
                fetched_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                api_url = apiUrl,
                data = aweme,
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(wrapper, Formatting.Indented));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("-", SourceName, $"儲存 aweme JSON 失敗: {ex.Message}");
        }
    }

    // 串流下載影片至暫存檔後重新命名；下載超時、檔案過小或失敗時回傳對應狀態字串
    private async Task<string> DownloadVideoAsync(string videoUrl, string savePath, CancellationToken ct)
    {
        var tempPath = savePath + ".tmp";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_cfg.DownloadTimeoutSeconds));

            using var resp = await _http.GetAsync(
                videoUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();

            long minBytes = (long)(_cfg.MinFileSizeMb * 1024 * 1024);
            var contentLength = resp.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value < minBytes)
            {
                AppLogger.Warn("-", SourceName, $"檔案太小 ({contentLength.Value / 1048576.0:F2}MB)，跳過");
                return "too_small";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            long downloaded = 0;
            using (var fs = File.Create(tempPath))
            using (var stream = await resp.Content.ReadAsStreamAsync(cts.Token))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cts.Token)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                    downloaded += read;
                }
            }

            if (downloaded < minBytes)
            {
                File.Delete(tempPath);
                AppLogger.Warn("-", SourceName, $"下載後檔案太小 ({downloaded / 1048576.0:F2}MB)，跳過");
                return "too_small";
            }

            File.Move(tempPath, savePath, overwrite: true);
            AppLogger.Info("-", SourceName, $"下載成功 ({downloaded / 1048576.0:F2}MB)");
            return "success";
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            AppLogger.Warn("-", SourceName, "下載超時，跳過");
            return "timeout";
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            AppLogger.Error("-", SourceName, "下載失敗", ex);
            return "failed";
        }
    }

    // 呼叫 API 逐頁取得影片清單；對每頁所有未上傳影片立即批次下載（CDN URL 取得後馬上使用避免過期）。
    // 回傳第一筆成功下載的 ContentItem，其餘已下載影片由 AccountWorker.GetPendingUpload() 依序上傳。
    private async Task<ContentItem?> FetchAndDownloadAllAsync(string secUserId, CancellationToken ct)
    {
        long maxCursor = 0;
        int page = 1;

        while (!ct.IsCancellationRequested)
        {
            AppLogger.Info("-", SourceName, $"API 第 {page} 頁（cursor={maxCursor}）...");
            var apiUrl = BuildFetchUrl(secUserId, maxCursor);
            var response = await FetchUserVideosAsync(secUserId, maxCursor, ct);

            if (response?.Code != 200 || response.Data == null)
            {
                AppLogger.Warn("-", SourceName, $"API 回應異常: code={response?.Code}");
                return null;
            }

            SaveApiResponseJson(secUserId, apiUrl, page, response);

            var newVideos = response.Data.AwemeList
                .Where(v => IsVideoWithVisualContent(v) && !_contentLog.IsUploaded(v.AwemeId))
                .ToList();

            if (newVideos.Count > 0)
            {
                AppLogger.Info("-", SourceName, $"本頁發現 {newVideos.Count} 筆未上傳影片，開始批次下載...");
                ContentItem? first = null;

                foreach (var video in newVideos)
                {
                    if (ct.IsCancellationRequested) break;

                    var videoId = video.AwemeId;
                    var savePath = Path.Combine(_videosDir, $"{videoId}.mp4");
                    var preview = video.Desc.Length > 30 ? video.Desc[..30] + "…" : video.Desc;

                    SaveAwemeJson(videoId, video, apiUrl);

                    // Already on disk from a previous session
                    if (File.Exists(savePath))
                    {
                        AppLogger.Info("-", SourceName, $"已下載待上傳: {videoId} | {preview}");
                        if (!_contentLog.IsDownloaded(videoId))
                            _contentLog.MarkDownloaded(videoId, SourceName, savePath, video.Desc);
                        first ??= new ContentItem
                        {
                            FilePath = savePath,
                            Description = video.Desc,
                            SourceId = videoId,
                            SourceType = SourceName,
                        };
                        continue;
                    }

                    var downloadUrl = GetNoWatermarkUrl(video.Video!);
                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        AppLogger.Warn("-", SourceName, $"無下載連結，略過 {videoId}");
                        continue;
                    }

                    AppLogger.Info("-", SourceName, $"下載: {videoId} | {preview}");
                    var result = await DownloadVideoAsync(downloadUrl, savePath, ct);

                    if (result == "success")
                    {
                        _contentLog.MarkDownloaded(videoId, SourceName, savePath, video.Desc);
                        first ??= new ContentItem
                        {
                            FilePath = savePath,
                            Description = video.Desc,
                            SourceId = videoId,
                            SourceType = SourceName,
                        };
                    }
                    else
                    {
                        AppLogger.Warn("-", SourceName, $"下載失敗 ({result})，略過 {videoId}");
                    }
                }

                if (first != null) return first;
            }

            if (response.Data.HasMore == 0 || response.Data.MaxCursor == 0) break;
            maxCursor = response.Data.MaxCursor;
            page++;
        }

        AppLogger.Info("-", SourceName, "API 所有頁面均無新影片可下載");
        return null;
    }
}
