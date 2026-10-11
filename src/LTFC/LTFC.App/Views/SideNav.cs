using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LTFC
{
    /// <summary>
    /// 侧边栏：顶部为 LTFC 主标题（最大字号），中间为各功能入口。
    /// 结构示例：
    ///   ClassTell
    ///     ├ 消息
    ///     └ 过期
    ///   OpenChoose
    ///     └ 设置
    ///   AutoNotice
    ///     └ 设置
    ///   视频下载
    ///   ────────────
    ///   总设置
    /// 触摸友好：字号更大、行高更高，条目多时可用滚轮/触摸板滚动。
    /// </summary>
    internal sealed class SideNav : Control
    {
        private sealed class Entry
        {
            public Control Control;
            public string Key;
            public string ParentKey;
            public bool IsGroup;
            public bool IsTitle;
            public bool IsSettings;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<string, Entry> _byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Entry>> _children = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entry> _scrollable = new List<Entry>();

        private int _scroll;
        private int _contentBottom;
        private NavButton _title;
        private Entry _settingsEntry;
        private string _footerText = string.Empty;
        private string _footerSub = string.Empty;

        public SideNav()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = Theme.NavWidth;
            Dock = DockStyle.Left;
        }

        /// <summary>当前选中项变化（参数为页面键）。</summary>
        public event Action<string> SelectionChanged;

        public NavButton AddTitle(string text, string key)
        {
            _title = new NavButton
            {
                Text = text,
                IsTitle = true,
                Height = Theme.NavTitleHeight
            };
            _title.Click += (s, e) => Select(_title);
            Register(_title, key, false, true);
            return _title;
        }

        /// <summary>添加一个模块分组项（可展开出子项）。</summary>
        public NavButton AddGroup(string text, string key)
        {
            var item = new NavButton
            {
                Text = text,
                Level = 0,
                IsExpandable = false,
                Height = Theme.NavGroupHeight
            };
            item.Click += (s, e) => Select(item);
            // 箭头：只展开 / 收起，不切换页面
            item.ArrowClick += (s, e) => ToggleGroupOnly(key);
            Register(item, key, true, false);
            return item;
        }

        /// <summary>给某个分组添加一个子项（点击直接进入该模块的分页）。</summary>
        public NavButton AddChild(string parentKey, string text, string key)
        {
            var item = new NavButton
            {
                Text = text,
                Level = 1,
                Height = Theme.NavChildHeight
            };
            item.Click += (s, e) => Select(item);
            Register(item, key, false, false, parentKey);

            NavButton parent = FindButton(parentKey);
            if (parent != null) parent.IsExpandable = true;
            return item;
        }

        public void AddSeparator()
        {
            var separator = new NavSeparator();
            _entries.Add(new Entry { Control = separator });
            _scrollable.Add(_entries[_entries.Count - 1]);
            Controls.Add(separator);
        }

        /// <summary>底部“总设置”入口。</summary>
        public NavButton AddSettings(string text, string key)
        {
            var item = new NavButton
            {
                Text = text,
                Level = 0,
                Height = Theme.NavGroupHeight
            };
            item.Click += (s, e) => Select(item);
            Register(item, key, false, false);
            _settingsEntry = _byKey.ContainsKey(key) ? _byKey[key] : null;
            if (_settingsEntry != null)
            {
                _settingsEntry.IsSettings = true;
                _settingsEntry.IsGroup = false;
                _scrollable.Remove(_settingsEntry);
            }
            return item;
        }

        private void Register(NavButton button, string key, bool isGroup, bool isTitle, string parentKey = null)
        {
            var entry = new Entry
            {
                Control = button,
                Key = key,
                IsGroup = isGroup,
                IsTitle = isTitle,
                ParentKey = parentKey
            };

            _entries.Add(entry);
            if (!string.IsNullOrEmpty(key)) _byKey[key] = entry;
            if (!isTitle) _scrollable.Add(entry);
            Controls.Add(button);

            if (!string.IsNullOrEmpty(parentKey))
            {
                List<Entry> list;
                if (!_children.TryGetValue(parentKey, out list))
                {
                    list = new List<Entry>();
                    _children[parentKey] = list;
                }
                list.Add(entry);
                button.Visible = false;
            }
        }

        private NavButton FindButton(string key)
        {
            Entry entry;
            return _byKey.TryGetValue(key ?? string.Empty, out entry) ? entry.Control as NavButton : null;
        }

        /// <summary>某个分组当前是否展开。</summary>
        public bool IsGroupExpanded(string key)
        {
            NavButton button = FindButton(key);
            return button != null && button.Expanded;
        }

        /// <summary>展开 / 收起分组。</summary>
        public void ExpandGroup(string key, bool expand)
        {
            NavButton button = FindButton(key);
            if (button == null) return;

            button.Expanded = expand;
            List<Entry> children;
            if (_children.TryGetValue(key ?? string.Empty, out children))
            {
                foreach (Entry child in children) child.Control.Visible = expand;
            }

            RequestLayout();
        }

        /// <summary>切换分组的展开状态。</summary>
        public void ToggleGroup(string key)
        {
            ExpandGroup(key, !IsGroupExpanded(key));
        }

        /// <summary>
        /// 只切换展开状态，不改变当前选中页（侧边栏箭头点击使用）。
        /// 这样点箭头不会触发页面切换，也就不会带入页面切换的耗时逻辑。
        /// </summary>
        public void ToggleGroupOnly(string key)
        {
            ToggleGroup(key);
        }

        /// <summary>按页面键选中（可选择是否触发切换事件）。</summary>
        public void SelectByKey(string key, bool raiseEvent)
        {
            Entry target;
            _byKey.TryGetValue(key ?? string.Empty, out target);

            foreach (Entry entry in _entries)
            {
                var button = entry.Control as NavButton;
                if (button == null) continue;
                button.Selected = ReferenceEquals(entry, target);
            }

            // 选中子项时自动展开它所属的分组，并滚动到可见位置
            if (target != null && !string.IsNullOrEmpty(target.ParentKey))
            {
                ExpandGroup(target.ParentKey, true);   // 内部只做延迟排布
                FlushLayout();                          // 需要真实坐标：先排一次
                EnsureVisible(target.Control);
                RequestLayout();
            }

            Invalidate();
            if (raiseEvent) Raise(key);
        }

        private void Select(NavButton item)
        {
            foreach (Entry entry in _entries)
            {
                if (!ReferenceEquals(entry.Control, item)) continue;

                // 分组项：点击时展开 / 收起，同时进入该模块的概览页
                if (entry.IsGroup && _children.ContainsKey(entry.Key ?? string.Empty))
                {
                    ToggleGroup(entry.Key);
                }

                SelectByKey(entry.Key, true);
                return;
            }
        }

        private void Raise(string key)
        {
            Action<string> handler = SelectionChanged;
            if (handler == null) return;
            try { handler(key); }
            catch (Exception ex) { AppLog.Exception_("侧边栏切换失败", ex); }
        }

        public void SetFooter(string footerText, string footerSub)
        {
            _footerText = footerText ?? string.Empty;
            _footerSub = footerSub ?? string.Empty;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FlushLayout();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollByWheel(e.Delta);
        }

        /// <summary>滚轮 / 触摸板滚动（每个刻度滚动一行的距离）。</summary>
        public void ScrollByWheel(int wheelDelta)
        {
            if (wheelDelta == 0) return;
            int step = Math.Max(1, wheelDelta / 120) * Theme.S(46);
            _scroll -= step;
            if (_scroll < 0) _scroll = 0;
            RequestLayout();
        }

        private int FooterReserve { get { return _footerText.Length > 0 ? Theme.S(74) : Theme.S(24); } }

        private bool _layoutQueued;

        /// <summary>
        /// 合并多次排布请求：一次消息循环内只真正排布一次。
        /// 点击（尤其是连点折叠箭头）时不会因为反复 SetBounds 而卡顿。
        /// </summary>
        private void RequestLayout()
        {
            _layoutQueued = true;
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke((MethodInvoker)DeferredLayout);
                    return;
                }
            }
            catch (Exception)
            {
            }

            DeferredLayout();
        }

        /// <summary>延迟排布入口：若期间已经同步排布过则直接跳过。</summary>
        private void DeferredLayout()
        {
            if (!_layoutQueued) return;
            _layoutQueued = false;
            LayoutEntries();
        }

        /// <summary>立刻完成尚未执行的排布（需要真实坐标时使用）。</summary>
        private void FlushLayout()
        {
            _layoutQueued = false;
            LayoutEntries();
        }

        /// <summary>把某个条目滚动到可见范围内。</summary>
        public void EnsureVisible(Control control)
        {
            if (control == null || !control.Visible) return;

            int bottomLimit = Height - FooterReserve;
            if (control.Bottom > bottomLimit) _scroll += control.Bottom - bottomLimit;
            else if (control.Top < Theme.S(6)) _scroll -= Theme.S(6) - control.Top;
            if (_scroll < 0) _scroll = 0;
        }

        /// <summary>
        /// 重新排布条目：标题固定在顶部，“总设置”固定在底部，中间的内容可滚动。
        /// </summary>
        public void LayoutEntries()
        {
            int margin = Theme.S(10);
            int width = Math.Max(Theme.S(80), Width - margin * 2);
            int x = margin;
            int bottomLimit = Math.Max(Theme.S(160), Height - FooterReserve);

            int y = Theme.S(14);
            foreach (Entry entry in _entries)
            {
                if (entry.IsSettings) continue;

                if (entry.IsTitle)
                {
                    entry.Control.Visible = true;
                    entry.Control.SetBounds(x, y, width, entry.Control.Height);
                    y += entry.Control.Height + Theme.S(6);
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.ParentKey) && !IsGroupExpanded(entry.ParentKey))
                {
                    entry.Control.Visible = false;
                    continue;
                }

                entry.Control.Visible = true;
                entry.Control.SetBounds(x, y - _scroll, width, entry.Control.Height);
                y += entry.Control.Height + Theme.S(4);
            }

            _contentBottom = y;

            int maxScroll = Math.Max(0, _contentBottom - (bottomLimit - Theme.S(8)));
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0) _scroll = 0;

            if (_settingsEntry != null)
            {
                _settingsEntry.Control.Visible = true;
                int top = Math.Max(Theme.S(60), Height - FooterReserve - _settingsEntry.Control.Height);
                _settingsEntry.Control.SetBounds(x, top, width, _settingsEntry.Control.Height);
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Painting.SetupQuality(g);

            using (var brush = new SolidBrush(Theme.NavBackground))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            using (var pen = new Pen(Theme.Outline, Theme.BorderWidth))
            {
                g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            }

            if (_footerText.Length > 0)
            {
                int y = Height - Theme.S(46);
                Painting.DrawText(g, _footerText, Theme.Small, Theme.TextFaint,
                    new Rectangle(Theme.S(14), y, Width - Theme.S(28), Theme.S(20)), StringAlignment.Near);
                if (_footerSub.Length > 0)
                {
                    Painting.DrawText(g, _footerSub, Theme.Small, Theme.TextFaint,
                        new Rectangle(Theme.S(14), y + Theme.S(18), Width - Theme.S(28), Theme.S(20)), StringAlignment.Near);
                }
            }
        }
    }
}
