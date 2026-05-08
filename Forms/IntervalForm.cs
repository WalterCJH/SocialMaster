namespace SocialMaster.Forms;

public class IntervalForm : Form
{
    private NumericUpDown _nudMin = null!;
    private NumericUpDown _nudMax = null!;

    public int MinMinutes => (int)_nudMin.Value;
    public int MaxMinutes => (int)_nudMax.Value;

    // 建立發文間隔設定對話方塊，預填目前的最短與最長間隔（分鐘）
    public IntervalForm(int currentMin = 1150, int currentMax = 5250)
    {
        Text = "設定發文間隔時間";
        Size = new Size(400, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(14),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _nudMin = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1,
            Maximum = 99999,
            Value = currentMin,
        };
        _nudMax = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1,
            Maximum = 99999,
            Value = currentMax,
        };

        layout.Controls.Add(new Label { Text = "最短 (分鐘)", Anchor = AnchorStyles.Left, Padding = new Padding(0, 0, 0, 0) }, 0, 0);
        layout.Controls.Add(_nudMin, 1, 0);
        layout.Controls.Add(new Label { Text = "最長 (分鐘)", Anchor = AnchorStyles.Left, Padding = new Padding(0, 0, 0, 0) }, 0, 1);
        layout.Controls.Add(_nudMax, 1, 1);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        var btnOk = new Button { Text = "確定", Width = 80 };
        btnOk.Click += (_, _) =>
        {
            if (_nudMin.Value >= _nudMax.Value)
            {
                MessageBox.Show("最短間隔必須小於最長間隔", "輸入錯誤", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        btnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });
        layout.Controls.Add(new Panel(), 0, 2);
        layout.Controls.Add(btnPanel, 1, 2);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.Add(layout);
    }
}
