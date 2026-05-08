namespace SocialMaster.Platforms.X;

public static class XSelectors
{
    // Login detection — account switcher or home link only appears when logged in
    public static readonly string[] NavHomeIcon =
    [
        "[data-testid='SideNav_AccountSwitcher_Button']",
        "a[data-testid='AppTabBar_Home_Link']",
        "[aria-label='Profile']",
    ];

    // Compose / New post button
    public static readonly string[] ComposeButton =
    [
        "[data-testid='SideNav_NewTweet_Button']",
        "a[data-testid='SideNav_NewTweet_Button']",
        "[aria-label='Post']",
        "[aria-label='發文']",
    ];

    // Tweet text input (contenteditable div inside compose dialog)
    public static readonly string[] TweetTextArea =
    [
        "[data-testid='tweetTextarea_0']",
        "div[role='textbox'][aria-label*='Post']",
        "div[role='textbox'][aria-label*='推文']",
        "div[role='textbox'][aria-label*='tweet']",
        "div[role='textbox'][contenteditable='true']",
    ];

    // Hidden file input for media/video attach
    public const string FileInput = "input[data-testid='fileInput']";

    // Media attach button (visible button that triggers file picker)
    public static readonly string[] MediaButton =
    [
        "[data-testid='attachments']",
        "button[aria-label='媒體']",
        "button[aria-label='Media']",
        "button[aria-label='新增媒體']",
        "button[aria-label='Add media']",
        "button[aria-label='Add photos or video']",
    ];

    // Video upload progress — wait for it to disappear before posting
    public static readonly string[] UploadProgressBar =
    [
        "[data-testid='attachmentProgressBar']",
        "[role='progressbar']",
    ];

    // Post / Tweet submit button inside compose dialog
    public static readonly string[] PostButton =
    [
        "[data-testid='tweetButtonInline']",
        "[data-testid='tweetButton']",
        "button[aria-label='發文']",
        "button[aria-label='Post']",
        "button[aria-label='Tweet']",
    ];

    // Success indicators — toast notification or compose dialog closing
    public static readonly string[] UploadSuccess =
    [
        "[data-testid='toast']",
    ];

    // Popup dismiss buttons
    public static readonly string[] PopupDismissButtons =
    [
        "button:has-text('稍後再說')", "button:has-text('Not Now')",
        "button:has-text('跳過')",     "button:has-text('Skip')",
        "button:has-text('關閉')",     "button:has-text('Close')",
        "button:has-text('Dismiss')",
    ];
}
