using Newtonsoft.Json;
using SocialMaster.Models;

namespace SocialMaster.Services;

public class SessionManager
{
    private readonly string _accountsDir;
    private readonly List<AccountProfile> _accounts = new();

    public IReadOnlyList<AccountProfile> Accounts => _accounts;

    public SessionManager(string accountsDir)
    {
        _accountsDir = accountsDir;
    }

    // 掃描 accounts 目錄，讀取每個數字子目錄下的 account_profile.json 並載入帳號清單
    public void Load()
    {
        _accounts.Clear();
        if (!Directory.Exists(_accountsDir)) return;

        foreach (var dir in Directory.GetDirectories(_accountsDir))
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out _)) continue;

            var profilePath = Path.Combine(dir, "account_profile.json");
            AccountProfile profile;

            if (File.Exists(profilePath))
            {
                profile = JsonConvert.DeserializeObject<AccountProfile>(
                    File.ReadAllText(profilePath)) ?? new AccountProfile();
            }
            else
            {
                // Auto-create profile for directories that don't have one yet
                profile = new AccountProfile { Id = int.Parse(name) };
            }

            profile.AccountDir = dir;
            EnsureAccountDirs(profile);
            _accounts.Add(profile);
        }

        _accounts.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    // 確保目錄存在後，將帳號設定序列化寫入 account_profile.json
    public void SaveProfile(AccountProfile profile)
    {
        EnsureAccountDirs(profile);
        File.WriteAllText(
            Path.Combine(profile.AccountDir, "account_profile.json"),
            JsonConvert.SerializeObject(profile, Formatting.Indented));
    }

    // 建立新帳號：分配最小可用 ID、建立目錄結構、儲存設定並加入清單
    public AccountProfile CreateAccount(string platform, string sourceType, string sourceConfig)
    {
        var id = NextAvailableId();
        var dir = Path.Combine(_accountsDir, id.ToString());
        var profile = new AccountProfile
        {
            Id = id,
            Platform = platform,
            SourceType = sourceType,
            SourceConfig = sourceConfig,
            AccountDir = dir,
        };
        EnsureAccountDirs(profile);
        SaveProfile(profile);
        _accounts.Add(profile);
        _accounts.Sort((a, b) => a.Id.CompareTo(b.Id));
        return profile;
    }

    // 更新既有帳號設定：儲存檔案並同步更新記憶體中的清單
    public void UpdateProfile(AccountProfile profile)
    {
        SaveProfile(profile);
        var idx = _accounts.FindIndex(a => a.Id == profile.Id);
        if (idx >= 0) _accounts[idx] = profile;
    }

    // 確保帳號所需的子目錄（AccountDir、browser、videos）皆已建立
    private static void EnsureAccountDirs(AccountProfile profile)
    {
        Directory.CreateDirectory(profile.AccountDir);
        Directory.CreateDirectory(profile.BrowserDir);
        Directory.CreateDirectory(profile.VideosDir);
    }

    // 從 1 開始找到第一個未被任何帳號使用的 ID
    private int NextAvailableId()
    {
        for (int i = 1; ; i++)
            if (_accounts.All(a => a.Id != i))
                return i;
    }
}
