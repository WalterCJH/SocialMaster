using SocialMaster.Models;

namespace SocialMaster.Forms;

public class AddAccountForm : Form
{
    private ComboBox? _cbPlatform;
    private readonly string _editPlatform;
    private ComboBox _cbSource = null!;
    private TextBox _txtSourceConfig = null!;
    private TextBox _txtCustomName = null!;
    private TextBox _txtNotes = null!;
    private Label _lblFacebookPageUrl = null!;
    private TextBox _txtFacebookPageUrl = null!;
    private Label _lblIsBusinessAccount = null!;
    private CheckBox _chkIsBusinessAccount = null!;
    private NumericUpDown _nudMinInterval = null!;
    private NumericUpDown _nudMaxInterval = null!;
    private TableLayoutPanel _layout = null!;
    private const int FacebookRowIndex = 5;
    private const int InstagramRowIndex = 6;
    private const int ConditionalRowHeight = 34;
    private const int BaseFormHeight = 340;

    public string SelectedPlatform => _cbPlatform?.Text ?? _editPlatform;
    public string SelectedSource => _cbSource.Text;
    public string SourceConfig => _txtSourceConfig.Text.Trim();
    public string CustomName => _txtCustomName.Text.Trim();
    public string Notes => _txtNotes.Text.Trim();
    public string FacebookPageUrl => _txtFacebookPageUrl.Text.Trim();
    public bool IsBusinessAccount => _chkIsBusinessAccount.Checked;
    public int MinIntervalMinutes => (int)_nudMinInterval.Value;
    public int MaxIntervalMinutes => (int)_nudMaxInterval.Value;

    // 建立新增或編輯帳號的對話方塊；傳入 existing 時切換為編輯模式，平台欄位變為唯讀標籤
    public AddAccountForm(
        IEnumerable<string> platforms,
        IEnumerable<string> sources,
        AccountProfile? existing = null)
    {
        bool isEdit = existing != null;
        _editPlatform = existing?.Platform ?? "";

        Text = isEdit ? $"編輯帳號 #{existing!.Id}" : "新增帳號";
        Size = new Size(650, BaseFormHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(14, 12, 14, 8),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Platform
        Control platformControl;
        if (isEdit)
        {
            platformControl = new Label
            {
                Text = _editPlatform,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 100, 200),
            };
        }
        else
        {
            _cbPlatform = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cbPlatform.Items.AddRange(platforms.ToArray<object>());
            _cbPlatform.SelectedIndex = 0;
            platformControl = _cbPlatform;
        }

        _cbSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _cbSource.Items.AddRange(sources.ToArray<object>());
        _cbSource.SelectedIndex = 0;
        if (existing != null) _cbSource.SelectedItem = existing.SourceType;

        _txtSourceConfig     = new TextBox { Dock = DockStyle.Fill, Text = existing?.SourceConfig ?? "" };
        _txtCustomName       = new TextBox { Dock = DockStyle.Fill, Text = existing?.CustomName ?? "" };
        _txtNotes            = new TextBox { Dock = DockStyle.Fill, Text = existing?.Notes ?? "" };
        _lblFacebookPageUrl  = MakeLabel("粉絲團網址 (FB)");
        _txtFacebookPageUrl  = new TextBox { Dock = DockStyle.Fill, Text = existing?.FacebookPageUrl ?? "" };
        _lblIsBusinessAccount = MakeLabel("商業帳號 (IG)");
        _chkIsBusinessAccount = new CheckBox
        {
            Text = "此為商業帳號",
            Dock = DockStyle.Fill,
            Checked = existing?.IsBusinessAccount ?? false,
            Padding = new Padding(0, 4, 0, 0),
        };

        // Interval row — [NUD] ~ [NUD] 分鐘
        _nudMinInterval = new NumericUpDown { Minimum = 1, Maximum = 99999, Width = 90, Value = existing?.MinIntervalMinutes ?? 1150 };
        _nudMaxInterval = new NumericUpDown { Minimum = 1, Maximum = 99999, Width = 90, Value = existing?.MaxIntervalMinutes ?? 5250 };
        var intervalPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0),
        };
        intervalPanel.Controls.Add(_nudMinInterval);
        intervalPanel.Controls.Add(new Label { Text = " ～ ", AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
        intervalPanel.Controls.Add(_nudMaxInterval);
        intervalPanel.Controls.Add(new Label { Text = "分鐘", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });

        // Buttons
        var btnOk = new Button { Text = "確定", Width = 80 };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        btnOk.Click += (_, _) =>
        {
            if (_nudMinInterval.Value >= _nudMaxInterval.Value)
            {
                MessageBox.Show("最短間隔必須小於最長間隔", "輸入錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        btnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });

        layout.Controls.Add(MakeLabel(isEdit ? "社群平台 (唯讀)" : "社群平台"), 0, 0);
        layout.Controls.Add(platformControl, 1, 0);
        layout.Controls.Add(MakeLabel("內容來源"), 0, 1);
        layout.Controls.Add(_cbSource, 1, 1);
        layout.Controls.Add(MakeLabel("來源設定 (URL)"), 0, 2);
        layout.Controls.Add(_txtSourceConfig, 1, 2);
        layout.Controls.Add(MakeLabel("自訂名稱"), 0, 3);
        layout.Controls.Add(_txtCustomName, 1, 3);
        layout.Controls.Add(MakeLabel("備註"), 0, 4);
        layout.Controls.Add(_txtNotes, 1, 4);
        layout.Controls.Add(_lblFacebookPageUrl, 0, 5);
        layout.Controls.Add(_txtFacebookPageUrl, 1, 5);
        layout.Controls.Add(_lblIsBusinessAccount, 0, 6);
        layout.Controls.Add(_chkIsBusinessAccount, 1, 6);
        layout.Controls.Add(MakeLabel("發文間隔"), 0, 7);
        layout.Controls.Add(intervalPanel, 1, 7);
        layout.Controls.Add(new Panel(), 0, 8);
        layout.Controls.Add(btnPanel, 1, 8);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.Add(layout);

        _layout = layout;
        UpdatePlatformDependentFields(SelectedPlatform);
        if (_cbPlatform != null)
            _cbPlatform.SelectedIndexChanged += (_, _) => UpdatePlatformDependentFields(_cbPlatform.Text);
    }

    // 依目前選擇的平台切換 FB 粉絲團網址 / IG 商業帳號欄位的顯示，並收合未使用的列避免留白
    private void UpdatePlatformDependentFields(string platform)
    {
        bool showFb = platform == "Facebook";
        bool showIg = platform == "Instagram";

        _lblFacebookPageUrl.Visible = showFb;
        _txtFacebookPageUrl.Visible = showFb;
        _layout.RowStyles[FacebookRowIndex] = new RowStyle(SizeType.Absolute, showFb ? ConditionalRowHeight : 0);

        _lblIsBusinessAccount.Visible = showIg;
        _chkIsBusinessAccount.Visible = showIg;
        _layout.RowStyles[InstagramRowIndex] = new RowStyle(SizeType.Absolute, showIg ? ConditionalRowHeight : 0);

        int extra = (showFb ? ConditionalRowHeight : 0) + (showIg ? ConditionalRowHeight : 0);
        Height = BaseFormHeight + extra;
    }

    // 建立左欄標籤，統一設定對齊與內距
    private static Label MakeLabel(string text) => new Label
    {
        Text = text,
        Anchor = AnchorStyles.Left | AnchorStyles.Top,
        Padding = new Padding(0, 8, 0, 0),
        AutoSize = true,
    };
}
