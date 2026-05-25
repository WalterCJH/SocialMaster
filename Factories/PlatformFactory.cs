using SocialMaster.Abstractions;
using SocialMaster.Models;
using SocialMaster.Platforms.Facebook;
using SocialMaster.Platforms.Instagram;
using SocialMaster.Platforms.X;

namespace SocialMaster.Factories;

public static class PlatformFactory
{
    private static readonly Dictionary<string, Func<PlatformConfig, ISocialPlatform>> Registry = new()
    {
        ["X"] = cfg => new XPlatform(cfg),
    };

    // 根據平台名稱建立對應的 ISocialPlatform 實作；Facebook 需要粉絲團網址、Instagram 需要商業帳號旗標，其餘平台從 Registry 建立
    public static ISocialPlatform Create(string platformName, SocialConfig config, AccountProfile? profile = null)
    {
        var platformCfg = config.Platforms.TryGetValue(platformName, out var cfg)
            ? cfg
            : new PlatformConfig();

        if (platformName == "Facebook")
            return new FacebookPlatform(platformCfg, profile?.FacebookPageUrl ?? "");

        if (platformName == "Instagram")
            return new InstagramPlatform(platformCfg, profile?.IsBusinessAccount ?? false);

        if (!Registry.TryGetValue(platformName, out var factory))
            throw new NotSupportedException($"Platform '{platformName}' is not registered.");

        return factory(platformCfg);
    }

    // 傳回所有支援的平台名稱
    public static IEnumerable<string> SupportedPlatforms =>
        new[] { "Instagram" }.Concat(Registry.Keys).Append("Facebook");
}
