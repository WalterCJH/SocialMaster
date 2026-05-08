using Newtonsoft.Json;
using SocialMaster.Models;

namespace SocialMaster.Services;

public class ConfigService
{
    private const string ConfigPath = "social_config.json";
    private SocialConfig _config = new();

    public SocialConfig Config => _config;

    // 從 social_config.json 讀取設定；若檔案不存在則建立預設設定並儲存
    public void Load()
    {
        if (!File.Exists(ConfigPath))
        {
            _config = CreateDefault();
            Save();
            return;
        }
        var json = File.ReadAllText(ConfigPath);
        _config = JsonConvert.DeserializeObject<SocialConfig>(json) ?? CreateDefault();
    }

    // 將目前設定序列化為 JSON 並寫入 social_config.json
    public void Save()
    {
        var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
        File.WriteAllText(ConfigPath, json);
    }

    // 建立包含 Instagram 平台與 Douyin 來源預設值的初始設定物件
    private static SocialConfig CreateDefault() => new()
    {
        Platforms = new Dictionary<string, PlatformConfig>
        {
            ["Instagram"] = new PlatformConfig
            {
                BaseUrl = "https://www.instagram.com/",
                UploadTimeoutSeconds = 120,
                NavigationTimeoutSeconds = 60,
                DefaultMinIntervalHours = 3,
                DefaultMaxIntervalHours = 8,
                DefaultCaptionHashtags = new List<string> { "#trending", "#video", "#content" },
                AccountNurturing = new NurturingConfig(),
            }
        },
        Sources = new Dictionary<string, Newtonsoft.Json.Linq.JObject>
        {
            ["Douyin"] = Newtonsoft.Json.Linq.JObject.FromObject(new
            {
                api_host = "api.tikhub.io",
                auth_token = "",
                min_file_size_mb = 1.0,
                download_timeout_seconds = 180,
                page_size = 20,
                max_attempts_per_page = 3,
            })
        }
    };
}
