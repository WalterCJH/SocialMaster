namespace SocialMaster.Platforms.Facebook;

public static class FacebookSelectors
{
    // Login detection — nav elements only present when logged in
    public static readonly string[] NavHomeIcon =
    [
        "a[aria-label='首頁']",
        "a[aria-label='Home']",
        "[aria-label='你的個人檔案']",
        "[aria-label='Your profile']",
        "[aria-label='Profile']",
    ];

    // Caption / description field in Reel creation Details step
    public static readonly string[] CaptionArea =
    [
        "div[aria-label='說明'][contenteditable]",
        "div[aria-label='Description'][contenteditable]",
        "div[aria-label='新增說明'][contenteditable]",
        "div[aria-label='Add a description'][contenteditable]",
        "div[contenteditable='true'][role='textbox']",
        "div[contenteditable='true']",
    ];

    // Upload progress bar inside Reel creator
    public static readonly string[] UploadProgressBar =
    [
        "[role='progressbar']",
        "[aria-valuemin='0']",
    ];

    // Success indicators — span text that appears after clicking 發佈, confirming the Reel was submitted
    // From DOM: <span>你的 Reel 正在處理中。Reel 就緒時，我們會通知你。</span>
    public static readonly string[] UploadSuccess =
    [
        "span:has-text('你的 Reel 正在處理中')",
        "span:has-text('Your reel is being processed')",
        "span:has-text('Reel 就緒時')",
    ];

    // Popup dismiss buttons
    public static readonly string[] PopupDismissButtons =
    [
        "div[role='button']:has-text('稍後再說')", "div[role='button']:has-text('Not Now')",
        "div[role='button']:has-text('關閉')",     "div[role='button']:has-text('Close')",
        "div[role='button']:has-text('略過')",     "div[role='button']:has-text('Skip')",
        "button:has-text('稍後再說')",             "button:has-text('Not Now')",
        "button:has-text('關閉')",                 "button:has-text('Close')",
        // "無法讀取檔案" error dialog close button
        "div[aria-label='關閉'][role='button']",   "div[aria-label='Close'][role='button']",
    ];
}
