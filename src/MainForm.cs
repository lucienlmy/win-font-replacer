using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace FontSwap
{
    internal enum HotSpot
    {
        None,
        Close,
        Apply,
        Restore,
        Restart,
        Toggle
    }

    internal sealed class MainForm : Form
    {
        private const int DesignWidth = 600;
        private const int DesignHeight = 584;

        private static readonly Rectangle TitleBarRect = new Rectangle(0, 0, DesignWidth, 48);
        private static readonly Rectangle CloseButtonRect = new Rectangle(548, 10, 28, 28);
        private static readonly Rectangle StatusCardRect = new Rectangle(18, 52, 564, 80);
        private static readonly Rectangle FontCardRect = new Rectangle(18, 140, 564, 216);
        private static readonly Rectangle SearchFieldRect = new Rectangle(32, 172, 536, 28);
        private static readonly Rectangle ListFrameRect = new Rectangle(32, 208, 536, 124);
        private static readonly Rectangle PreviewCardRect = new Rectangle(18, 364, 564, 100);
        private static readonly Rectangle PreviewBoxRect = new Rectangle(32, 392, 536, 56);
        private static readonly Rectangle ToggleTrackRect = new Rectangle(32, 474, 36, 20);
        private static readonly Rectangle ToggleHitRect = new Rectangle(18, 464, 340, 48);
        private static readonly Rectangle ApplyButtonRect = new Rectangle(18, 512, 250, 36);
        private static readonly Rectangle RestoreButtonRect = new Rectangle(280, 512, 132, 36);
        private static readonly Rectangle RestartButtonRect = new Rectangle(424, 512, 158, 36);

        private float _scale = 1f;

        private TextBox _search;
        private ListBox _fontList;

        private List<string> _filtered = new List<string>();
        private string _selectedFont = string.Empty;
        private bool _suppressSelect;
        private int _listHot = -1;

        private bool _replaceIcon;
        private bool _hasBackup;
        private string _backupFont = string.Empty;
        private string _currentFont = string.Empty;
        private string _legacyNotice = string.Empty;
        private int _menuHeight = -12;

        private Font _fontTitle;
        private Font _fontSubtitle;
        private Font _fontSection;
        private Font _fontLabel;
        private Font _fontBody;
        private Font _fontValue;
        private Font _fontSmall;
        private Font _fontTiny;
        private Font _fontButton;
        private Font _fontListFont;
        private Font _fontBadge;
        private Font _fontIcon;

        private readonly Dictionary<string, Font> _sampleFonts = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        private HotSpot _hot = HotSpot.None;
        private HotSpot _pressed = HotSpot.None;

        private string _toastText;
        private bool _toastError;
        private DateTime _toastUntil = DateTime.MinValue;
        private Timer _toastTimer;

        public MainForm()
        {
            Text = "Windows 全局字体替换器";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.WindowBack;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            _replaceIcon = SettingsStore.IncludeIconFont;
            _selectedFont = SettingsStore.LastFont;

            InitializeFonts();
            CreateControls();
            TryImportLegacyBackup();
            RefreshState();
        }

        private void InitializeFonts()
        {
            Font fallback = SystemFonts.MessageBoxFont;
            _fontTitle = FontCatalog.CreateFont(Theme.UiFont, 12f, FontStyle.Bold, fallback);
            _fontSubtitle = FontCatalog.CreateFont(Theme.UiFont, 8f, FontStyle.Regular, fallback);
            _fontSection = FontCatalog.CreateFont(Theme.UiFont, 10f, FontStyle.Bold, fallback);
            _fontLabel = FontCatalog.CreateFont(Theme.UiFont, 8f, FontStyle.Regular, fallback);
            _fontBody = FontCatalog.CreateFont(Theme.UiFont, 9f, FontStyle.Regular, fallback);
            _fontValue = FontCatalog.CreateFont(Theme.UiFont, 13.5f, FontStyle.Bold, fallback);
            _fontSmall = FontCatalog.CreateFont(Theme.UiFont, 8f, FontStyle.Regular, fallback);
            _fontTiny = FontCatalog.CreateFont(Theme.UiFont, 7.5f, FontStyle.Regular, fallback);
            _fontButton = FontCatalog.CreateFont(Theme.UiFont, 9.5f, FontStyle.Regular, fallback);
            _fontListFont = FontCatalog.CreateFont(Theme.UiFont, 9.5f, FontStyle.Regular, fallback);
            _fontBadge = FontCatalog.CreateFont(Theme.UiFont, 7.5f, FontStyle.Regular, fallback);
            _fontIcon = FontCatalog.CreateFont(Theme.UiFont, 9.5f, FontStyle.Bold, fallback);
        }

        private void CreateControls()
        {
            _search = new TextBox();
            _search.BorderStyle = BorderStyle.None;
            _search.Font = FontCatalog.CreateFont(Theme.UiFont, 10f, FontStyle.Regular, _fontBody);
            _search.BackColor = Color.FromArgb(249, 250, 251);
            _search.ForeColor = Theme.TextPrimary;
            _search.AutoSize = false;
            _search.TextChanged += delegate { ReloadFontList(_search.Text); };
            _search.Enter += delegate { Invalidate(); };
            _search.Leave += delegate { Invalidate(); };
            _search.KeyDown += SearchKeyDown;
            Controls.Add(_search);

            _fontList = new ListBox();
            _fontList.BorderStyle = BorderStyle.None;
            _fontList.BackColor = Color.White;
            _fontList.ForeColor = Theme.TextPrimary;
            _fontList.DrawMode = DrawMode.OwnerDrawFixed;
            _fontList.IntegralHeight = false;
            _fontList.Font = _fontListFont;
            _fontList.DrawItem += FontListItemDraw;
            _fontList.SelectedIndexChanged += delegate
            {
                if (_suppressSelect)
                {
                    return;
                }
                string name = _fontList.SelectedItem as string;
                if (!string.IsNullOrEmpty(name))
                {
                    _selectedFont = name;
                    Invalidate();
                }
            };
            _fontList.MouseMove += FontListMouseMove;
            _fontList.MouseLeave += delegate
            {
                if (_listHot != -1)
                {
                    _listHot = -1;
                    _fontList.Invalidate();
                }
            };
            _fontList.DoubleClick += delegate { ApplySelectedFont(); };
            _fontList.KeyDown += FontListKeyDown;
            Controls.Add(_fontList);

            _toastTimer = new Timer();
            _toastTimer.Interval = 300;
            _toastTimer.Tick += delegate
            {
                if (!string.IsNullOrEmpty(_toastText) && DateTime.UtcNow > _toastUntil)
                {
                    _toastText = null;
                    Invalidate();
                }
            };
            _toastTimer.Start();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ComputeScale();
            ApplyLayout();
            ReloadFontList(string.Empty);
            if (string.IsNullOrEmpty(_selectedFont))
            {
                _selectedFont = _currentFont;
            }
            ReloadFontList(_search.Text);
            if (!string.IsNullOrEmpty(_legacyNotice))
            {
                ShowToast(_legacyNotice, false);
                _legacyNotice = string.Empty;
            }
            Invalidate();
        }

        /// <summary>升级用户：旧版 1.x 的数据目录里如果有一份基线，接管它作为回退备份。</summary>
        private void TryImportLegacyBackup()
        {
            try
            {
                string message;
                if (LegacyImporter.TryImport(out message))
                {
                    _legacyNotice = message;
                }
            }
            catch
            {
            }
        }

        private void ComputeScale()
        {
            _scale = 1f;
            try
            {
                using (Graphics graphics = CreateGraphics())
                {
                    if (graphics.DpiX > 0f)
                    {
                        _scale = graphics.DpiX / 96f;
                    }
                }
            }
            catch
            {
                _scale = 1f;
            }

            if (_scale < 0.75f || _scale > 4f)
            {
                _scale = 1f;
            }

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            float verticalLimit = (float)(area.Height - 16) / DesignHeight;
            if (verticalLimit > 0.6f && _scale > verticalLimit)
            {
                _scale = verticalLimit;
            }
        }

        private void ApplyLayout()
        {
            int width = S(DesignWidth);
            int height = S(DesignHeight);
            ClientSize = new Size(width, height);

            _search.Bounds = S(new Rectangle(56, 175, 504, 22));
            _fontList.ItemHeight = S(30);
            _fontList.Bounds = S(new Rectangle(34, 210, 532, 120));
            UpdateRegion();
            CenterOnScreen();
        }

        private void CenterOnScreen()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int x = area.X + (area.Width - Width) / 2;
            int y = area.Y + (area.Height - Height) / 2;
            if (x < area.X)
            {
                x = area.X;
            }
            if (y < area.Y)
            {
                y = area.Y;
            }
            Location = new Point(x, y);
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = Theme.RoundedRect(new Rectangle(0, 0, Width, Height), S(14)))
            {
                Region = new Region(path);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_search != null)
            {
                UpdateRegion();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (SolidBrush back = new SolidBrush(Theme.WindowBack))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            PaintTitleBar(g);
            PaintStatusCard(g);
            PaintFontCard(g);
            PaintPreviewCard(g);
            PaintOptions(g);
            PaintButtons(g);
            PaintFooter(g);
            PaintToast(g);
        }

        private void PaintTitleBar(Graphics g)
        {
            using (Pen pen = new Pen(Theme.Divider, 1f))
            {
                g.DrawLine(pen, 0, S(48), Width, S(48));
            }

            Rectangle icon = S(new Rectangle(18, 10, 28, 28));
            using (GraphicsPath path = Theme.RoundedRect(icon, S(8)))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                icon,
                Color.FromArgb(96, 165, 250),
                Color.FromArgb(37, 99, 235),
                LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(brush, path);
            }
            DrawCentered(g, "Aa", _fontIcon, Color.White, icon);

            Draw(g, "Windows 全局字体替换器", _fontTitle, Theme.TextPrimary, P(54, 9));
            Draw(g, "系统界面字体替换", _fontSubtitle, Theme.TextMuted, P(54, 27));

            Rectangle close = S(CloseButtonRect);
            if (_hot == HotSpot.Close)
            {
                Theme.FillRounded(g, close, S(7), Color.FromArgb(233, 235, 239));
            }
            PaintCloseGlyph(g, close);
        }

        private void PaintCloseGlyph(Graphics g, Rectangle rect)
        {
            int pad = Math.Max(7, rect.Width / 3);
            Color color = _hot == HotSpot.Close ? Theme.TextPrimary : Theme.TextSecondary;
            using (Pen pen = new Pen(color, 1.4f))
            {
                g.DrawLine(pen, rect.Left + pad, rect.Top + pad, rect.Right - pad, rect.Bottom - pad);
                g.DrawLine(pen, rect.Right - pad, rect.Top + pad, rect.Left + pad, rect.Bottom - pad);
            }
        }

        private void PaintStatusCard(Graphics g)
        {
            Theme.DrawCard(g, S(StatusCardRect), S(10), Theme.CardBack, Theme.CardBorder);

            Draw(g, "当前界面字体", _fontLabel, Theme.TextSecondary, P(32, 64));

            string current = string.IsNullOrEmpty(_currentFont) ? "（无法读取）" : _currentFont;
            DrawRect(g, current, _fontValue, Theme.TextPrimary, R(32, 78, 330, 22),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            Rectangle badge = R(462, 64, 104, 20);
            Theme.FillRounded(g, badge, S(10), Theme.SuccessSoft);
            DrawCentered(g, "字号未改动", _fontBadge, Theme.Success, badge);

            DrawRight(g, "跟随系统 DPI 与缩放", _fontTiny, Theme.TextMuted, R(380, 88, 186, 14));

            bool applied = _hasBackup;
            Rectangle dot = R(32, 106, 6, 6);
            using (SolidBrush brush = new SolidBrush(applied ? Theme.Success : Theme.TextMuted))
            {
                g.FillEllipse(brush, dot);
            }

            string status;
            if (applied)
            {
                status = "已应用替换，可一键回退";
                if (!string.IsNullOrEmpty(_backupFont))
                {
                    status += " · 原始字体：" + _backupFont;
                }
            }
            else
            {
                status = "尚未替换字体，当前为系统原始设置";
            }

            DrawRect(g, status, _fontSmall, applied ? Theme.TextSecondary : Theme.TextMuted, R(44, 102, 500, 14),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }

        private void PaintFontCard(Graphics g)
        {
            Theme.DrawCard(g, S(FontCardRect), S(10), Theme.CardBack, Theme.CardBorder);

            Draw(g, "选择字体", _fontSection, Theme.TextPrimary, P(32, 150));
            DrawRight(g, "共 " + _filtered.Count + " 款", _fontSmall, Theme.TextMuted, R(420, 151, 146, 14));

            Rectangle field = S(SearchFieldRect);
            Theme.FillRounded(g, field, S(8), Color.FromArgb(249, 250, 251));
            Theme.StrokeRounded(g, field, S(8), _search.Focused ? Theme.Accent : Theme.CardBorder, 1f);

            PaintSearchIcon(g, P(46, 185));

            Rectangle frame = S(ListFrameRect);
            Theme.FillRounded(g, frame, S(8), Color.White);
            Theme.StrokeRounded(g, frame, S(8), Theme.CardBorder, 1f);

            if (_filtered.Count == 0)
            {
                DrawCentered(g, "没有找到匹配的字体", _fontBody, Theme.TextMuted, frame);
            }
            else if (_fontList.SelectedIndex < 0 && !string.IsNullOrEmpty(_search.Text))
            {
                DrawRight(g, "按 Enter 应用", _fontTiny, Theme.TextMuted, R(360, 338, 206, 12));
            }

            Draw(g, "只替换字形，字号与图标大小保持不变。", _fontTiny, Theme.TextMuted, P(32, 340));
        }

        private void PaintSearchIcon(Graphics g, Point center)
        {
            Color color = _search.Focused ? Theme.Accent : Theme.TextMuted;
            using (Pen pen = new Pen(color, 1.3f))
            {
                g.DrawEllipse(pen, center.X - 4, center.Y - 4, 8, 8);
                g.DrawLine(pen, center.X + 2.6f, center.Y + 2.6f, center.X + 6.4f, center.Y + 6.4f);
            }
        }

        private void PaintPreviewCard(Graphics g)
        {
            Theme.DrawCard(g, S(PreviewCardRect), S(10), Theme.CardBack, Theme.CardBorder);

            Draw(g, "预览", _fontSection, Theme.TextPrimary, P(32, 374));

            float size = FontCatalog.PointSizeForHeight(_menuHeight);
            DrawRight(g, "菜单字号 " + size.ToString("0.#") + " pt · 未改动", _fontTiny, Theme.TextMuted, R(330, 375, 236, 14));

            Rectangle box = S(PreviewBoxRect);
            Theme.FillRounded(g, box, S(8), Color.FromArgb(250, 250, 251));

            string family = _selectedFont;
            if (string.IsNullOrEmpty(family))
            {
                family = _currentFont;
            }
            if (string.IsNullOrEmpty(family))
            {
                family = Theme.UiFont;
            }

            using (Font menuFont = FontCatalog.CreateFont(family, size, FontStyle.Regular, _fontBody))
            using (Font bodyFont = FontCatalog.CreateFont(family, 9f, FontStyle.Regular, _fontBody))
            {
                DrawRect(g, "文件(F)    编辑(E)    查看(V)    帮助(H)", menuFont, Theme.TextPrimary, R(44, 396, 512, 22),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
                DrawRect(g, "字体替换只改变字形，不改变字号与图标大小。", bodyFont, Theme.TextSecondary, R(44, 422, 512, 18),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            }
        }

        private void PaintOptions(Graphics g)
        {
            Rectangle track = S(ToggleTrackRect);
            Color trackColor = _replaceIcon ? Theme.Accent : Color.FromArgb(203, 213, 225);
            Theme.FillRounded(g, track, track.Height / 2, trackColor);

            int knobSize = track.Height - S(4);
            Rectangle knob = _replaceIcon
                ? new Rectangle(track.Right - knobSize - S(2), track.Top + S(2), knobSize, knobSize)
                : new Rectangle(track.Left + S(2), track.Top + S(2), knobSize, knobSize);
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                g.FillEllipse(brush, knob);
            }

            Color labelColor = _hot == HotSpot.Toggle ? Theme.Accent : Theme.TextPrimary;
            Draw(g, "同时替换桌面图标文字", _fontBody, labelColor, P(78, 470));
            Draw(g, "只改字体名，不影响图标大小与间距", _fontTiny, Theme.TextMuted, P(78, 489));
        }

        private void PaintButtons(Graphics g)
        {
            PaintButton(g, ApplyButtonRect, "应用字体", true, true, HotSpot.Apply);
            PaintButton(g, RestoreButtonRect, "一键回退", false, _hasBackup, HotSpot.Restore);
            PaintButton(g, RestartButtonRect, "重启资源管理器", false, true, HotSpot.Restart);
        }

        private void PaintButton(Graphics g, Rectangle designRect, string text, bool primary, bool enabled, HotSpot spot)
        {
            Rectangle rect = S(designRect);
            bool hot = enabled && _hot == spot;
            bool down = hot && _pressed == spot;

            if (!enabled)
            {
                Theme.FillRounded(g, rect, S(9), Color.FromArgb(246, 247, 249));
                Theme.StrokeRounded(g, rect, S(9), Theme.CardBorder, 1f);
                DrawCentered(g, text, _fontButton, Color.FromArgb(183, 187, 194), rect);
                return;
            }

            if (primary)
            {
                Color color = down ? Theme.AccentDown : (hot ? Theme.AccentHover : Theme.Accent);
                Theme.FillRounded(g, rect, S(9), color);
                DrawCentered(g, text, _fontButton, Color.White, rect);
            }
            else
            {
                Color color = hot ? Theme.NeutralButtonHover : Theme.NeutralButton;
                Theme.FillRounded(g, rect, S(9), color);
                Theme.StrokeRounded(g, rect, S(9), Theme.NeutralButtonBorder, 1f);
                DrawCentered(g, text, _fontButton, Theme.TextPrimary, rect);
            }
        }

        private void PaintFooter(Graphics g)
        {
            DrawCentered(g, "应用后部分窗口未刷新时可重启资源管理器；随时可一键回退。",
                _fontTiny, Theme.TextMuted, R(18, 554, 564, 14));
        }

        private void PaintToast(Graphics g)
        {
            if (string.IsNullOrEmpty(_toastText) || DateTime.UtcNow > _toastUntil)
            {
                return;
            }

            Rectangle toast = R(32, 548, 536, 30);
            Color back = _toastError ? Theme.ToastErrorBack : Theme.ToastBack;
            Theme.FillRounded(g, toast, S(9), Color.FromArgb(240, back));
            DrawCentered(g, _toastText, _fontSmall, Color.White, toast);
        }

        private void FontListItemDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _fontList.Items.Count)
            {
                return;
            }

            string name = _fontList.Items[e.Index] as string;
            Rectangle row = e.Bounds;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Rectangle fill = new Rectangle(row.Left + S(4), row.Top + S(2), row.Width - S(8), row.Height - S(4));

            if (selected)
            {
                Theme.FillRounded(g, fill, S(7), Theme.AccentSoft);
                Theme.StrokeRounded(g, fill, S(7), Theme.AccentSoftBorder, 1f);
            }
            else if (_listHot == e.Index)
            {
                Theme.FillRounded(g, fill, S(7), Color.FromArgb(246, 247, 249));
            }

            Color nameColor = selected ? Theme.Accent : Theme.TextPrimary;
            Rectangle nameRect = new Rectangle(row.Left + S(12), row.Top + S(6), S(146), S(20));
            TextRenderer.DrawText(g, name, _fontListFont, nameRect, nameColor,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

            Font sample = GetSampleFont(name);
            Rectangle sampleRect = new Rectangle(row.Left + S(164), row.Top + S(6), row.Width - S(176), S(20));
            TextRenderer.DrawText(g, "Aa 字体预览 123", sample, sampleRect,
                selected ? Theme.Accent : Theme.TextMuted,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }

        private Font GetSampleFont(string family)
        {
            Font cached;
            if (_sampleFonts.TryGetValue(family, out cached))
            {
                return cached;
            }

            Font created = FontCatalog.CreateFont(family, 9f, FontStyle.Regular, _fontListFont);
            _sampleFonts[family] = created;
            return created;
        }

        private void FontListMouseMove(object sender, MouseEventArgs e)
        {
            int index = _fontList.IndexFromPoint(e.Location);
            if (index != _listHot)
            {
                _listHot = index;
                _fontList.Invalidate();
            }
        }

        private void SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && _fontList.Items.Count > 0)
            {
                _fontList.Focus();
                if (_fontList.SelectedIndex < 0)
                {
                    _fontList.SelectedIndex = 0;
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                ApplySelectedFont();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void FontListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplySelectedFont();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ReloadFontList(string query)
        {
            _suppressSelect = true;
            try
            {
                _filtered = FontCatalog.Filter(query);

                _fontList.BeginUpdate();
                _fontList.Items.Clear();
                for (int i = 0; i < _filtered.Count; i++)
                {
                    _fontList.Items.Add(_filtered[i]);
                }
                _fontList.EndUpdate();

                _fontList.Visible = _filtered.Count > 0;

                int index = FindFontIndex(_filtered, _selectedFont);
                if (index < 0)
                {
                    index = FindFontIndex(_filtered, _currentFont);
                }
                if (index < 0 && _filtered.Count > 0)
                {
                    index = 0;
                    _selectedFont = _filtered[0];
                }
                if (index >= 0)
                {
                    _fontList.SelectedIndex = index;
                    if (index > 1)
                    {
                        _fontList.TopIndex = index - 1;
                    }
                }
                else
                {
                    _fontList.ClearSelected();
                }
            }
            finally
            {
                _suppressSelect = false;
            }

            Invalidate();
        }

        private static int FindFontIndex(List<string> list, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private void RefreshState()
        {
            try
            {
                _currentFont = MetricReader.CurrentInterfaceFont();
            }
            catch
            {
                _currentFont = string.Empty;
            }

            try
            {
                _menuHeight = MetricReader.MenuFontHeight();
            }
            catch
            {
                _menuHeight = -12;
            }

            try
            {
                _hasBackup = FontOperations.HasBackup();
                _backupFont = _hasBackup ? FontOperations.OriginalFont() : string.Empty;
            }
            catch
            {
                _hasBackup = false;
                _backupFont = string.Empty;
            }
        }

        private void ApplySelectedFont()
        {
            string face = _selectedFont;
            if (string.IsNullOrEmpty(face))
            {
                ShowToast("请先在列表中选择一个字体。", true);
                return;
            }

            try
            {
                ApplyResult result = FontOperations.Apply(face, _replaceIcon);
                SettingsStore.LastFont = face;
                SettingsStore.IncludeIconFont = _replaceIcon;
                RefreshState();
                Invalidate();
                ShowToast("已应用「" + result.FontFamily + "」，字号保持不变。", false);
            }
            catch (Exception ex)
            {
                RefreshState();
                Invalidate();
                ShowToast(ex.Message, true);
            }
        }

        private void RestoreFonts()
        {
            if (!Confirm("确定要恢复到替换前的原始字体吗？\n\n六项界面字体将写回原始值，字号不受影响。"))
            {
                return;
            }

            try
            {
                ApplyResult result = FontOperations.Restore();
                RefreshState();
                Invalidate();
                ShowToast("已恢复原始字体设置。", false);
                OperationLog.Write("用户回退字体，此前的目标字体为：" + result.FontFamily);
            }
            catch (Exception ex)
            {
                RefreshState();
                Invalidate();
                ShowToast(ex.Message, true);
            }
        }

        private void RestartExplorer()
        {
            if (!Confirm("将重启 Windows 资源管理器，任务栏会短暂消失后自动恢复。\n\n确定继续吗？"))
            {
                return;
            }

            ShowToast("正在重启资源管理器…", false);
            try
            {
                FontOperations.RestartExplorer();
                ShowToast("资源管理器已重启。", false);
            }
            catch (Exception ex)
            {
                ShowToast(ex.Message, true);
            }
        }

        private bool Confirm(string text)
        {
            return MessageBox.Show(this, text, "Windows 全局字体替换器", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void ToggleIconFont()
        {
            _replaceIcon = !_replaceIcon;
            SettingsStore.IncludeIconFont = _replaceIcon;
            Invalidate();
        }

        private void ShowToast(string text, bool error)
        {
            _toastText = text;
            _toastError = error;
            _toastUntil = DateTime.UtcNow.AddSeconds(error ? 4.5 : 3.0);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            HotSpot hit = HitTest(e.Location);
            if (hit == HotSpot.None && S(TitleBarRect).Contains(e.Location) && !S(CloseButtonRect).Contains(e.Location))
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessagePtr(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                    new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
                return;
            }

            _pressed = hit;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            HotSpot hit = HitTest(e.Location);
            if (hit != _hot)
            {
                _hot = hit;
                Cursor = hit == HotSpot.None ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            HotSpot hit = HitTest(e.Location);
            HotSpot pressed = _pressed;
            _pressed = HotSpot.None;

            if (pressed != HotSpot.None && pressed == hit)
            {
                switch (pressed)
                {
                    case HotSpot.Close:
                        Close();
                        break;
                    case HotSpot.Apply:
                        ApplySelectedFont();
                        break;
                    case HotSpot.Restore:
                        RestoreFonts();
                        break;
                    case HotSpot.Restart:
                        RestartExplorer();
                        break;
                    case HotSpot.Toggle:
                        ToggleIconFont();
                        break;
                }
            }

            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != HotSpot.None)
            {
                _hot = HotSpot.None;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        private HotSpot HitTest(Point point)
        {
            if (S(CloseButtonRect).Contains(point))
            {
                return HotSpot.Close;
            }
            if (S(ApplyButtonRect).Contains(point))
            {
                return HotSpot.Apply;
            }
            if (_hasBackup && S(RestoreButtonRect).Contains(point))
            {
                return HotSpot.Restore;
            }
            if (S(RestartButtonRect).Contains(point))
            {
                return HotSpot.Restart;
            }
            if (S(ToggleHitRect).Contains(point))
            {
                return HotSpot.Toggle;
            }
            return HotSpot.None;
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        private Rectangle S(Rectangle rect)
        {
            return new Rectangle(S(rect.X), S(rect.Y), S(rect.Width), S(rect.Height));
        }

        private Rectangle R(int x, int y, int width, int height)
        {
            return S(new Rectangle(x, y, width, height));
        }

        private Point P(int x, int y)
        {
            return new Point(S(x), S(y));
        }

        private void Draw(Graphics g, string text, Font font, Color color, Point point)
        {
            TextRenderer.DrawText(g, text, font, point, color, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        private void DrawRect(Graphics g, string text, Font font, Color color, Rectangle rect, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, rect, color, flags);
        }

        private void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle rect)
        {
            TextRenderer.DrawText(g, text, font, rect, color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawRight(Graphics g, string text, Font font, Color color, Rectangle rect)
        {
            TextRenderer.DrawText(g, text, font, rect, color,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_toastTimer != null)
                {
                    _toastTimer.Stop();
                    _toastTimer.Dispose();
                    _toastTimer = null;
                }

                foreach (Font font in _sampleFonts.Values)
                {
                    font.Dispose();
                }
                _sampleFonts.Clear();

                DisposeFont(ref _fontTitle);
                DisposeFont(ref _fontSubtitle);
                DisposeFont(ref _fontSection);
                DisposeFont(ref _fontLabel);
                DisposeFont(ref _fontBody);
                DisposeFont(ref _fontValue);
                DisposeFont(ref _fontSmall);
                DisposeFont(ref _fontTiny);
                DisposeFont(ref _fontButton);
                DisposeFont(ref _fontListFont);
                DisposeFont(ref _fontBadge);
                DisposeFont(ref _fontIcon);
            }

            base.Dispose(disposing);
        }

        private static void DisposeFont(ref Font font)
        {
            if (font != null)
            {
                font.Dispose();
                font = null;
            }
        }
    }
}