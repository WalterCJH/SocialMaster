namespace SocialMaster.Platforms.Instagram;

public static class InstagramSelectors
{
    // Login detection - nav bar home icon
    public static readonly string[] NavHomeIcon =
    [
        "[aria-label='首頁']", "[aria-label='Home']",
        "svg[aria-label='首頁']", "svg[aria-label='Home']",
        "a[href='/'][role='link']",
    ];

    // Create / New post button - try many possible labels across locales
    public static readonly string[] CreateButton =
    [
        "[aria-label='新增']",
        "[aria-label='New post']",
        "[aria-label='建立']",
        "[aria-label='Create']",
        "[aria-label='新增貼文']",
        "svg[aria-label='新增']",
        "svg[aria-label='New post']",
        "svg[aria-label='建立']",
        "svg[aria-label='Create']",
    ];

    // After clicking Create, a menu may appear - pick "Reel" or "Post"
    public static readonly string[] ReelMenuItem =
    [
        "[role='menuitem']:has-text('短片')",
        "[role='menuitem']:has-text('Reel')",
        "span:has-text('短片')",
        "span:has-text('Reel')",
    ];

    // Popup dismiss buttons (notifications, cookie consent, etc.)
    public static readonly string[] PopupDismissButtons =
    [
        "button:has-text('稍後再說')", "button:has-text('Not Now')",
        "button:has-text('以後再說')", "button:has-text('Maybe Later')",
        "button:has-text('關閉')", "button:has-text('Close')",
        "button:has-text('略過')", "button:has-text('Skip')",
        "[role='button']:has-text('稍後再說')", "[role='button']:has-text('Not Now')",
    ];

    // Confirmation popup after file selection (format warning, duration warning, etc.)
    public static readonly string[] ConfirmPopupButtons =
    [
        "button:has-text('確定')", "button:has-text('OK')",
        "button:has-text('好')",   "button:has-text('繼續')",
        "button:has-text('繼續使用')", "button:has-text('Continue')",
        "[role='button']:has-text('確定')", "[role='button']:has-text('OK')",
    ];

    // "影片貼文現在會以 Reel 形式分享" info popup — aria-modal dialog containing h2 with "Reel"/"影片貼文",
    // dismissed by clicking the "確定"/"OK" button inside (identified by text content, not fragile CSS classes)
    public const string ReelInfoPopupDialog = "[aria-modal='true'][role='dialog']";

    // "從電腦選擇" button inside the "建立新貼文" modal
    public static readonly string[] SelectFromComputerButton =
    [
        "button._aswp",
        "button:has-text('從電腦選擇')",
        "button:has-text('Select from computer')",
        "button:has-text('選擇檔案')",
        "button:has-text('Choose file')",
    ];

    // File input lives inside <form role="presentation"> — it is hidden in the DOM
    public const string FileInput = "form[role='presentation'] input[type='file']";

    // Crop screen heading — detects that the crop/ratio step appeared
    public static readonly string[] CropScreenHeading =
    [
        "h3:has-text('裁切')", "h3:has-text('Crop')",
        "[aria-label='裁切']",  "[aria-label='Crop']",
    ];

    // Aspect-ratio selector button — SVG title "選擇「裁切」", class _aswq
    public static readonly string[] RatioSelectorButton =
    [
        "button:has(svg[aria-label='選擇「裁切」'])",
        "button._aswq",
        "svg[aria-label='選擇「裁切」']",
        "button:has(svg[aria-label='Select crop'])",
        "svg[aria-label='Select crop']",
    ];

    // "原始" (Original) aspect-ratio option that appears after clicking the ratio button
    public static readonly string[] OriginalRatioOption =
    [
        "span:has-text('原始')",   "button:has-text('原始')",
        "span:has-text('Original')", "button:has-text('Original')",
        "[aria-label='原始']",
    ];

    public static readonly string[] NextButton =
    [
        "button:has-text('下一步')", "button:has-text('Next')",
        "div[role='button']:has-text('下一步')", "div[role='button']:has-text('Next')",
    ];

    public static readonly string[] ShareButton =
    [
        "button:has-text('分享')", "button:has-text('Share')",
        "div[role='button']:has-text('分享')", "div[role='button']:has-text('Share')",
    ];

    public static readonly string[] CaptionArea =
    [
        "div[data-lexical-editor='true']",           // most stable — Lexical editor marker
        "div[aria-label='撰寫說明文字……']",            // two ellipsis chars
        "div[aria-label='Write a caption...']",
        "div[aria-label='撰寫說明文字…']",
        "div[aria-label='Write a caption…']",
        "div[role='textbox'][contenteditable='true']",
    ];

    public static readonly string[] UploadSuccess =
    [
        "h3:has-text('我們已分享你的 Reel')",
        "h3:has-text('Your Reel has been shared')",
        "h1:has-text('已分享')", "h1:has-text('Your reel has been shared')",
        "h1:has-text('貼文已分享')", "h1:has-text('Post shared')",
        "span:has-text('已分享')",
    ];
}
