using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    internal enum DialogTone
    {
        Info,
        Warn,
        Error,
        Question
    }

    /// <summary>一个对话框请求（内容 + 选项）。</summary>
    internal sealed class DialogRequest
    {
        public DialogRequest()
        {
            Options = new List<string>();
            Buttons = new List<string>();
            DefaultButtonIndex = 0;
        }

        public string Title { get; set; }
        public string Message { get; set; }
        public string Detail { get; set; }
        public DialogTone Tone { get; set; }

        /// <summary>需要在多个选项中选择时的候选项（为空表示纯提示/确认）。</summary>
        public List<string> Options { get; set; }

        /// <summary>按钮文字（默认“确定”）。</summary>
        public List<string> Buttons { get; set; }

        public int DefaultButtonIndex { get; set; }

        /// <summary>是否显示输入框。</summary>
        public bool ShowInput { get; set; }

        public string InputLabel { get; set; }
        public string InputText { get; set; }

        /// <summary>是否显示勾选框（例如“不再提示”）。</summary>
        public string CheckBoxText { get; set; }
        public bool CheckBoxValue { get; set; }
    }

    /// <summary>对话框结果。</summary>
    internal sealed class DialogResultInfo
    {
        public DialogResultInfo()
        {
            SelectedIndex = -1;
            ButtonIndex = 0;
        }

        public int SelectedIndex { get; set; }
        public int ButtonIndex { get; set; }
        public string InputText { get; set; }
        public bool CheckBoxChecked { get; set; }
        public bool Accepted { get; set; }
    }

    /// <summary>
    /// 统一对话框：所有需要用户判断 / 确认 / 选择的交互都走这里，
    /// 高对比度模式下使用明显的边框与高对比度配色。
    /// </summary>
    internal sealed class DialogForm : Form
    {
        private readonly DialogRequest _request;
        private readonly List<UiButton> _buttons = new List<UiButton>();
        private ListBox _list;
        private TextBox _input;
        private CheckBox _check;
        private Label _message;
        private Label _detailLabel;
        private Label _inputLabel;

        private DialogForm(DialogRequest request)
        {
            _request = request;
            BuildUi();
        }

        public DialogResultInfo Result { get; private set; }

        /// <summary>以模态方式显示（owner 为空时居中屏幕）。</summary>
        public static DialogResultInfo Show(IWin32Window owner, DialogRequest request)
        {
            using (var form = new DialogForm(request))
            {
                if (owner != null) form.ShowDialog(owner);
                else form.ShowDialog();
                return form.Result;
            }
        }

        private void BuildUi()
        {
            Text = string.IsNullOrEmpty(_request.Title) ? Theme.ShortName : _request.Title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Icon = IconFactory.AppIcon;
            KeyPreview = true;
            Padding = new Padding(Theme.S(18));

            _message = new Label
            {
                Text = _request.Message ?? string.Empty,
                AutoSize = false,
                Font = Theme.Font(1f, FontStyle.Regular),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            };
            Controls.Add(_message);

            if (!string.IsNullOrEmpty(_request.Detail))
            {
                _detailLabel = new Label
                {
                    Text = _request.Detail,
                    AutoSize = false,
                    Font = Theme.Small,
                    ForeColor = Theme.TextMuted,
                    BackColor = Color.Transparent
                };
                Controls.Add(_detailLabel);
            }

            if (_request.Options != null && _request.Options.Count > 0)
            {
                _list = new ListBox
                {
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = Theme.Font(0.5f, FontStyle.Regular),
                    BackColor = Theme.HighContrast ? Color.White : Theme.Surface,
                    ForeColor = Theme.Text,
                    IntegralHeight = false,
                    DrawMode = DrawMode.OwnerDrawFixed,
                    ItemHeight = (int)Math.Round(Theme.Font(0.5f, FontStyle.Regular).GetHeight() * 1.8f)
                };
                _list.DrawItem += OnDrawListItem;
                foreach (string option in _request.Options) _list.Items.Add(option);
                _list.SelectedIndex = 0;
                _list.DoubleClick += (s, e) => Accept();
                Controls.Add(_list);
            }

            if (_request.ShowInput)
            {
                if (!string.IsNullOrEmpty(_request.InputLabel))
                {
                    _inputLabel = new Label
                    {
                        Text = _request.InputLabel,
                        AutoSize = false,
                        Font = Theme.Small,
                        ForeColor = Theme.TextMuted,
                        BackColor = Color.Transparent
                    };
                    Controls.Add(_inputLabel);
                }

                _input = new TextBox
                {
                    Text = _request.InputText ?? string.Empty,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = Theme.Font(0.5f, FontStyle.Regular),
                    BackColor = Color.White,
                    ForeColor = Theme.Text
                };
                Controls.Add(_input);
            }

            if (!string.IsNullOrEmpty(_request.CheckBoxText))
            {
                _check = new CheckBox
                {
                    Text = _request.CheckBoxText,
                    Checked = _request.CheckBoxValue,
                    AutoSize = true,
                    Font = Theme.Font(0f, FontStyle.Regular),
                    ForeColor = Theme.Text,
                    FlatStyle = Theme.HighContrast ? FlatStyle.Standard : FlatStyle.Flat,
                    BackColor = Color.Transparent
                };
                Controls.Add(_check);
            }

            List<string> buttonTexts = _request.Buttons != null && _request.Buttons.Count > 0
                ? _request.Buttons
                : new List<string> { "确定" };

            for (int i = 0; i < buttonTexts.Count; i++)
            {
                var button = new UiButton
                {
                    Text = buttonTexts[i],
                    Kind = i == _request.DefaultButtonIndex
                        ? (_request.Tone == DialogTone.Error ? UiButtonKind.Danger : UiButtonKind.Primary)
                        : UiButtonKind.Secondary,
                    PaddingX = 20,
                    Height = Theme.S(38)
                };
                int index = i;
                button.Click += (s, e) => { _pendingButtonIndex = index; Accept(); };
                Controls.Add(button);
                _buttons.Add(button);
            }

            KeyDown += OnDialogKeyDown;

            LayoutDialog();
        }

        private void OnDialogKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Cancel();
            }
            else if (e.KeyCode == Keys.Enter && (_input == null || !_input.Focused))
            {
                Accept();
            }
        }

        private void OnDrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? Theme.Accent : (Theme.HighContrast ? Color.White : Theme.Surface);
            Color fore = selected ? Theme.AccentOn : Theme.Text;
            using (var brush = new SolidBrush(back)) g.FillRectangle(brush, e.Bounds);
            Painting.DrawText(g, Convert.ToString(_list.Items[e.Index]), _list.Font, fore,
                new Rectangle(e.Bounds.X + Theme.S(6), e.Bounds.Y, e.Bounds.Width - Theme.S(12), e.Bounds.Height),
                StringAlignment.Near);
            if (Theme.HighContrast)
            {
                using (var pen = new Pen(Theme.Outline, 1f)) g.DrawRectangle(pen, e.Bounds);
            }
        }

        private int _pendingButtonIndex = -1;

        private void LayoutDialog()
        {
            int contentWidth = Theme.S(430);
            using (Graphics g = CreateGraphics())
            {
                if (_list != null && _request.Options != null && _request.Options.Count > 0)
                {
                    foreach (string option in _request.Options)
                    {
                        int need = (int)Math.Round(Painting.Measure(g, option, _list.Font).Width) + Theme.S(60);
                        if (need > contentWidth) contentWidth = need;
                    }
                }

                int x = Padding.Left;
                int y = Padding.Top;

                int messageHeight = MeasureTextHeight(_message.Text, _message.Font, contentWidth);
                _message.SetBounds(x, y, contentWidth, messageHeight);
                y += messageHeight + Theme.S(10);

                if (_detailLabel != null)
                {
                    int detailHeight = MeasureTextHeight(_detailLabel.Text, _detailLabel.Font, contentWidth);
                    _detailLabel.SetBounds(x, y, contentWidth, detailHeight);
                    y += detailHeight + Theme.S(10);
                }

                if (_list != null)
                {
                    int rows = Math.Min(7, Math.Max(2, _list.Items.Count));
                    int listHeight = rows * _list.ItemHeight + Theme.S(6);
                    _list.SetBounds(x, y, contentWidth, listHeight);
                    y += listHeight + Theme.S(10);
                }

                if (_inputLabel != null)
                {
                    int labelHeight = (int)Math.Round(Theme.LineHeight(g, _inputLabel.Font));
                    _inputLabel.SetBounds(x, y, contentWidth, labelHeight);
                    y += labelHeight + Theme.S(4);
                }

                if (_input != null)
                {
                    _input.SetBounds(x, y, contentWidth, Theme.S(30));
                    y += Theme.S(30) + Theme.S(10);
                }

                if (_check != null)
                {
                    _check.Location = new Point(x, y);
                    y += Math.Max(Theme.S(24), _check.Height) + Theme.S(10);
                }

                int buttonHeight = Theme.S(38);
                int gap = Theme.S(10);
                int totalWidth = 0;
                int[] widths = new int[_buttons.Count];
                for (int i = 0; i < _buttons.Count; i++)
                {
                    widths[i] = Math.Max(Theme.S(96), _buttons[i].MeasureWidth(g, _buttons[i].Text));
                    totalWidth += widths[i] + (i > 0 ? gap : 0);
                }

                int bx = x + Math.Max(0, contentWidth - totalWidth);
                for (int i = 0; i < _buttons.Count; i++)
                {
                    _buttons[i].SetBounds(bx, y, widths[i], buttonHeight);
                    bx += widths[i] + gap;
                }
                y += buttonHeight;

                ClientSize = new Size(Padding.Horizontal + contentWidth, Padding.Vertical + (y - Padding.Top));
            }
        }

        private static int MeasureTextHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return Theme.S(18);
            Size proposed = new Size(Math.Max(40, width), int.MaxValue);
            Size measured = TextRenderer.MeasureText(text, font, proposed, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return Math.Max(Theme.S(18), measured.Height + Theme.S(4));
        }

        private void Accept()
        {
            Result = new DialogResultInfo
            {
                Accepted = true,
                ButtonIndex = _pendingButtonIndex >= 0 ? _pendingButtonIndex : _request.DefaultButtonIndex,
                SelectedIndex = _list != null ? _list.SelectedIndex : -1,
                InputText = _input != null ? _input.Text : null,
                CheckBoxChecked = _check != null && _check.Checked
            };
            Close();
        }

        private void Cancel()
        {
            Result = new DialogResultInfo
            {
                Accepted = false,
                ButtonIndex = -1,
                SelectedIndex = _list != null ? _list.SelectedIndex : -1,
                InputText = _input != null ? _input.Text : null,
                CheckBoxChecked = _check != null && _check.Checked
            };
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Theme.HighContrast) return;

            // 高对比度：整体再描一圈明显的边框
            using (var pen = new Pen(Theme.Text, Math.Max(2f, Theme.BorderWidth * 1.6f)))
            {
                pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        /// <summary>记录按钮索引（供 Accept 使用）。</summary>
        public void SetPendingButtonIndex(int index)
        {
            _pendingButtonIndex = index;
        }
    }

    /// <summary>对话框快捷入口（提示 / 确认 / 选择 / 输入）。</summary>
    internal static class Ask
    {
        public static DialogResultInfo Custom(IWin32Window owner, DialogRequest request)
        {
            return DialogForm.Show(owner, request);
        }

        public static void Info(IWin32Window owner, string title, string message, string detail = null)
        {
            DialogForm.Show(owner, new DialogRequest
            {
                Title = title,
                Message = message,
                Detail = detail,
                Tone = DialogTone.Info,
                Buttons = new List<string> { "确定" }
            });
        }

        public static void Warn(IWin32Window owner, string title, string message, string detail = null)
        {
            DialogForm.Show(owner, new DialogRequest
            {
                Title = title,
                Message = message,
                Detail = detail,
                Tone = DialogTone.Warn,
                Buttons = new List<string> { "确定" }
            });
        }

        public static void Error(IWin32Window owner, string title, string message, string detail = null)
        {
            DialogForm.Show(owner, new DialogRequest
            {
                Title = title,
                Message = message,
                Detail = detail,
                Tone = DialogTone.Error,
                Buttons = new List<string> { "确定" }
            });
        }

        /// <summary>确认框：返回 true 表示用户选择了“确定”。</summary>
        public static bool Confirm(IWin32Window owner, string title, string message, string detail = null,
            string okText = "确定", string cancelText = "取消")
        {
            DialogResultInfo result = DialogForm.Show(owner, new DialogRequest
            {
                Title = title,
                Message = message,
                Detail = detail,
                Tone = DialogTone.Question,
                Buttons = new List<string> { okText, cancelText },
                DefaultButtonIndex = 0
            });
            return result != null && result.Accepted && result.ButtonIndex == 0;
        }

        /// <summary>选择框：返回选中项索引；取消返回 -1。</summary>
        public static int Choose(IWin32Window owner, string title, string message, IList<string> options, int defaultIndex = 0)
        {
            var request = new DialogRequest
            {
                Title = title,
                Message = message,
                Tone = DialogTone.Question,
                Buttons = new List<string> { "确定", "取消" },
                DefaultButtonIndex = 0
            };
            foreach (string option in options) request.Options.Add(option);
            DialogResultInfo result = DialogForm.Show(owner, request);
            if (result == null || !result.Accepted || result.ButtonIndex != 0) return -1;
            return result.SelectedIndex >= 0 ? result.SelectedIndex : defaultIndex;
        }

        /// <summary>输入框：返回输入内容；取消返回 null。</summary>
        public static string Prompt(IWin32Window owner, string title, string message, string label, string initial = null)
        {
            DialogResultInfo result = DialogForm.Show(owner, new DialogRequest
            {
                Title = title,
                Message = message,
                Tone = DialogTone.Question,
                InputLabel = label,
                InputText = initial,
                ShowInput = true,
                Buttons = new List<string> { "确定", "取消" },
                DefaultButtonIndex = 0
            });
            if (result == null || !result.Accepted || result.ButtonIndex != 0) return null;
            return result.InputText ?? string.Empty;
        }

        /// <summary>确认并给出“记住选择”勾选框：返回 (是否确认, 是否记住)。</summary>
        public static bool ConfirmWithCheck(IWin32Window owner, string title, string message, string detail,
            string checkText, ref bool checkValue, string okText = "确定", string cancelText = "取消")
        {
            DialogResultInfo result = DialogForm.Show(owner, new DialogRequest
            {
                Title = title,
                Message = message,
                Detail = detail,
                Tone = DialogTone.Question,
                CheckBoxText = checkText,
                CheckBoxValue = checkValue,
                Buttons = new List<string> { okText, cancelText },
                DefaultButtonIndex = 0
            });
            if (result == null) return false;
            checkValue = result.CheckBoxChecked;
            return result.Accepted && result.ButtonIndex == 0;
        }
    }
}
