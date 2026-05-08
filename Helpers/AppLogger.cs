using Serilog;
using Serilog.Core;

namespace SocialMaster.Helpers;

public static class AppLogger
{
    private static ILogger _logger = Logger.None;

    public static event EventHandler<LogEntry>? OnLog;

    // 初始化 Serilog，設定每日滾動的日誌檔案輸出
    public static void Initialize(string logPath)
    {
        _logger = new LoggerConfiguration()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    // 記錄 INFO 等級訊息，並透過 OnLog 事件通知 UI
    public static void Info(string accountId, string platform, string message)
    {
        _logger.Information("[{Platform}][{AccountId}] {Message}", platform, accountId, message);
        Raise(accountId, platform, "INFO", message);
    }

    // 記錄 WARN 等級訊息，並透過 OnLog 事件通知 UI
    public static void Warn(string accountId, string platform, string message)
    {
        _logger.Warning("[{Platform}][{AccountId}] {Message}", platform, accountId, message);
        Raise(accountId, platform, "WARN", message);
    }

    // 記錄 ERROR 等級訊息，可附帶例外物件，並透過 OnLog 事件通知 UI
    public static void Error(string accountId, string platform, string message, Exception? ex = null)
    {
        if (ex != null)
            _logger.Error(ex, "[{Platform}][{AccountId}] {Message}", platform, accountId, message);
        else
            _logger.Error("[{Platform}][{AccountId}] {Message}", platform, accountId, message);
        Raise(accountId, platform, "ERROR", message + (ex != null ? $" | {ex.Message}" : ""));
    }

    // 建立 LogEntry 並觸發 OnLog 事件，讓訂閱者（如 UI）即時顯示日誌
    private static void Raise(string accountId, string platform, string level, string message)
    {
        OnLog?.Invoke(null, new LogEntry
        {
            AccountId = accountId,
            Platform = platform,
            Level = level,
            Message = message,
            Timestamp = DateTime.Now,
        });
    }
}

public class LogEntry
{
    public string AccountId { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }

    // 格式化為 UI 日誌區顯示用的單行字串
    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}][{Level}][{Platform}][{AccountId}] {Message}";
}
