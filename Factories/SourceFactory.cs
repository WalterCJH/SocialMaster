using Newtonsoft.Json;
using SocialMaster.Abstractions;
using SocialMaster.Models;
using SocialMaster.Services;
using SocialMaster.Sources.Douyin;

namespace SocialMaster.Factories;

public static class SourceFactory
{
    // 根據來源名稱從設定建立對應的 IContentSource 實作；找不到時拋出例外
    public static IContentSource Create(
        string sourceName,
        ContentLogService contentLog,
        string videosDir,
        SocialConfig config)
    {
        var douyinCfg = config.Sources.TryGetValue(sourceName, out var raw)
            ? raw.ToObject<DouyinConfig>() ?? new DouyinConfig()
            : new DouyinConfig();

        return sourceName switch
        {
            "Douyin" => new DouyinSource(contentLog, videosDir, douyinCfg),
            // "YouTube" => new YouTubeSource(contentLog, videosDir, ytCfg),
            _ => throw new NotSupportedException($"Source '{sourceName}' is not registered.")
        };
    }

    // 傳回目前已支援的所有內容來源名稱
    public static IEnumerable<string> SupportedSources => new[] { "Douyin" };
}
