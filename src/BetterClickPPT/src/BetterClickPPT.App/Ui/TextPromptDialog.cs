namespace BetterClickPPT.App.Ui;

/// <summary>简单的单行文本输入对话框。</summary>
internal static class TextPromptDialog
{
    /// <summary>弹出输入框；取消时返回 null。</summary>
    public static string? Show(IWin32Window? owner, string title, string prompt, string defaultValue = "")
    {
        using var form = new Form
        {
            Text = title,
            ClientSize = new Size(420, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            Font = new Font("Microsoft YaHei UI", 9f),
            Icon = IconFactory.AppIcon
        };

        var label = new Label
        {
            Text = prompt,
            Location = new Point(16, 16),
            Size = new Size(388, 40)
        };

        var input = new TextBox
        {
            Text = defaultValue,
            Location = new Point(16, 60),
            Size = new Size(388, 24)
        };

        var okButton = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(228, 100),
            Size = new Size(84, 30)
        };

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(320, 100),
            Size = new Size(84, 30)
        };

        form.Controls.Add(label);
        form.Controls.Add(input);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }
}
