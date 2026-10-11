namespace BetterClickPPT.App.Setup;

/// <summary>安装向导页面。</summary>
internal abstract class WizardPage : UserControl
{
    protected WizardPage()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        AutoScroll = true;
    }

    /// <summary>标题（显示在向导顶部）。</summary>
    public abstract string Title { get; }

    /// <summary>副标题。</summary>
    public abstract string Subtitle { get; }

    /// <summary>“下一步”按钮文字。</summary>
    public virtual string NextText => "下一步";

    /// <summary>进入本页时调用。</summary>
    public virtual void OnEnter(SetupModel model)
    {
    }

    /// <summary>离开本页（点击下一步）时调用，用于把界面状态写回模型。</summary>
    public virtual void OnLeave(SetupModel model)
    {
    }

    /// <summary>是否可以离开本页（返回 false 时显示提示并停留在本页）。</summary>
    public virtual bool CanLeave(SetupModel model, out string? error)
    {
        error = null;
        return true;
    }

    /// <summary>创建统一样式的标签。</summary>
    protected static Label CreateLabel(string text, int x, int y, int width = 540, int height = 22)
        => new()
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            AutoSize = false
        };

    /// <summary>创建说明性灰色标签。</summary>
    protected static Label CreateHint(string text, int x, int y, int width = 540, int height = 40)
        => new()
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            AutoSize = false,
            ForeColor = Color.FromArgb(100, 116, 139)
        };
}
