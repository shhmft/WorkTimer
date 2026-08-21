using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WorkTimer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (Mutex mtx = new Mutex(true, "WorkTimerSingleInstance_9F3A", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Таймер уже запущен — ищите иконку в трее.", "WorkTimer",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                GC.KeepAlive(mtx);
            }
        }
    }

    // ================= ПАЛИТРА =================
    static class Skin
    {
        public static readonly Color Bg = Color.FromArgb(13, 15, 22);
        public static readonly Color Card = Color.FromArgb(21, 25, 35);
        public static readonly Color Card2 = Color.FromArgb(29, 34, 48);
        public static readonly Color Card3 = Color.FromArgb(38, 45, 63);
        public static readonly Color Line = Color.FromArgb(40, 47, 66);
        public static readonly Color Text = Color.FromArgb(232, 237, 246);
        public static readonly Color Muted = Color.FromArgb(124, 137, 163);
        public static readonly Color Dim = Color.FromArgb(86, 96, 119);
        public static readonly Color A1 = Color.FromArgb(129, 91, 255);
        public static readonly Color A2 = Color.FromArgb(34, 211, 238);
        public static readonly Color Green = Color.FromArgb(45, 212, 137);
        public static readonly Color Amber = Color.FromArgb(250, 176, 60);
        public static readonly Color Rose = Color.FromArgb(244, 96, 128);

        public static Font F(float size, FontStyle st) { return new Font("Segoe UI", size, st); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            if (rad <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    // ================= КНОПКА =================
    class GButton : Control
    {
        public enum Kind { Primary, Ghost, Icon }
        public Kind Style = Kind.Ghost;
        public Color A = Skin.A1, B = Skin.A2;
        public Color ParentBg = Skin.Bg;
        public float Radius = 12f;
        public bool Danger = false;
        bool hover, press;

        public GButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            ForeColor = Skin.Text;
            Font = Skin.F(9.5f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { press = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { press = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(ParentBg)) g.FillRectangle(bg, ClientRectangle);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath p = Skin.Round(r, Radius))
            {
                if (Style == Kind.Primary)
                {
                    Color ca = A, cb = B;
                    if (press) { ca = Skin.Mix(ca, Color.Black, 0.18f); cb = Skin.Mix(cb, Color.Black, 0.18f); }
                    else if (hover) { ca = Skin.Mix(ca, Color.White, 0.12f); cb = Skin.Mix(cb, Color.White, 0.12f); }
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new Rectangle(0, 0, Width, Height), ca, cb, LinearGradientMode.Horizontal))
                        g.FillPath(lg, p);
                    if (hover)
                        using (Pen gl = new Pen(Color.FromArgb(90, Color.White), 1f)) g.DrawPath(gl, p);
                }
                else if (Style == Kind.Ghost)
                {
                    Color f = press ? Skin.Card3 : (hover ? Skin.Card2 : Color.FromArgb(26, 31, 44));
                    using (SolidBrush sb = new SolidBrush(f)) g.FillPath(sb, p);
                    using (Pen pn = new Pen(hover ? Skin.Card3 : Skin.Line, 1f)) g.DrawPath(pn, p);
                }
                else
                {
                    if (hover)
                        using (SolidBrush sb = new SolidBrush(Danger
                            ? Color.FromArgb(220, 60, 80) : Skin.Card2))
                            g.FillPath(sb, p);
                }
            }

            Color tc = Style == Kind.Primary ? Color.White
                : (hover ? (Danger ? Color.White : Skin.Text) : ForeColor);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, tc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // ================= СПИСОК ДНЕЙ =================
    class DayRow
    {
        public DateTime Day;
        public double Minutes;
        public bool Manual;
        public bool IsToday;
        public bool Weekend;
    }

    class DayList : Control
    {
        public List<DayRow> Rows = new List<DayRow>();
        public double BarScale = 480;
        public Action<DateTime> DayActivated;
        public string EmptyText = "Пока пусто — нажми «Старт»";

        int scroll = 0;
        int hoverIdx = -1;
        const int RowH = 30;

        public DayList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = false;
            BackColor = Skin.Card;
            Font = Skin.F(9.5f, FontStyle.Regular);
        }

        int MaxScroll { get { return Math.Max(0, Rows.Count * RowH - Height); } }

        public void SetRows(List<DayRow> rows, double scale)
        {
            Rows = rows; BarScale = scale;
            if (scroll > MaxScroll) scroll = MaxScroll;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (CanFocus && !Focused) Focus();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int old = scroll;
            scroll -= (e.Delta / 120) * RowH * 2;
            if (scroll < 0) scroll = 0;
            if (scroll > MaxScroll) scroll = MaxScroll;
            if (scroll != old) Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = (e.Y + scroll) / RowH;
            if (idx < 0 || idx >= Rows.Count) idx = -1;
            if (idx != hoverIdx) { hoverIdx = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoverIdx != -1) { hoverIdx = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int idx = (e.Y + scroll) / RowH;
            if (idx >= 0 && idx < Rows.Count && DayActivated != null)
                DayActivated(Rows[idx].Day);
            base.OnMouseDoubleClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

            if (Rows.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Skin.F(9.5f, FontStyle.Regular),
                    ClientRectangle, Skin.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");
            int barX = 118;
            int barW = Width - barX - 82;
            if (barW < 40) barW = 40;

            int first = Math.Max(0, scroll / RowH);
            int last = Math.Min(Rows.Count - 1, (scroll + Height) / RowH);

            for (int i = first; i <= last; i++)
            {
                DayRow r = Rows[i];
                int y = i * RowH - scroll;

                if (i == hoverIdx)
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(28, 33, 47)))
                    using (GraphicsPath hp = Skin.Round(new RectangleF(0, y + 1, Width - 6, RowH - 2), 8))
                        g.FillPath(hb, hp);

                if (r.IsToday)
                    using (SolidBrush ab = new SolidBrush(Skin.A2))
                    using (GraphicsPath ap = Skin.Round(new RectangleF(0, y + 8, 3, RowH - 16), 1.5f))
                        g.FillPath(ab, ap);

                Color dc = r.Weekend ? Skin.Rose : Skin.Text;
                TextRenderer.DrawText(g, r.Day.ToString("dd.MM"),
                    Skin.F(9.5f, r.IsToday ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(12, y, 56, RowH), dc,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, ru.TextInfo.ToTitleCase(r.Day.ToString("ddd", ru)),
                    Skin.F(8.5f, FontStyle.Regular),
                    new Rectangle(66, y, 48, RowH), r.Weekend ? Skin.Rose : Skin.Dim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                RectangleF track = new RectangleF(barX, y + RowH / 2f - 3.5f, barW, 7);
                using (GraphicsPath tp = Skin.Round(track, 3.5f))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(30, 36, 51)))
                    g.FillPath(tb, tp);

                double frac = BarScale > 0 ? r.Minutes / BarScale : 0;
                if (frac > 1) frac = 1;
                float w = (float)(barW * frac);
                if (w > 3)
                {
                    RectangleF fill = new RectangleF(barX, track.Y, w, 7);
                    using (GraphicsPath fp = Skin.Round(fill, 3.5f))
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new RectangleF(barX, track.Y, barW, 7), Skin.A1, Skin.A2, LinearGradientMode.Horizontal))
                        g.FillPath(lg, fp);
                }

                string hrs = TrayApp.Fmt(r.Minutes) + (r.Manual ? " *" : "");
                TextRenderer.DrawText(g, hrs, Skin.F(9.5f, FontStyle.Bold),
                    new Rectangle(Width - 78, y, 66, RowH),
                    r.IsToday ? Skin.A2 : Skin.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (MaxScroll > 0)
            {
                float th = Math.Max(24f, (float)Height * Height / (Rows.Count * RowH));
                float ty = (float)scroll / MaxScroll * (Height - th);
                using (GraphicsPath sp = Skin.Round(new RectangleF(Width - 4, ty, 3, th), 1.5f))
                using (SolidBrush sb = new SolidBrush(Skin.Card3))
                    g.FillPath(sb, sp);
            }
        }
    }

    // ================= ДАННЫЕ =================
    class Segment
    {
        public DateTime Start;
        public DateTime End;
        public Segment(DateTime s, DateTime e) { Start = s; End = e; }
    }

    static class Store
    {
        public static string Dir
        {
            get
            {
                string d = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkTimer");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string SessionsFile { get { return Path.Combine(Dir, "sessions.csv"); } }
        public static string OverridesFile { get { return Path.Combine(Dir, "overrides.csv"); } }
        public static string SettingsFile { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Dictionary<string, string> LoadSettings()
        {
            Dictionary<string, string> s = new Dictionary<string, string>();
            if (!File.Exists(SettingsFile)) return s;
            try
            {
                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) s[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
            return s;
        }

        public static void SaveSettings(Dictionary<string, string> s)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in s)
                    sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(SettingsFile, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static List<Segment> LoadSegments()
        {
            List<Segment> list = new List<Segment>();
            if (!File.Exists(SessionsFile)) return list;
            foreach (string line in File.ReadAllLines(SessionsFile))
            {
                string s = line.Trim();
                if (s.Length == 0) continue;
                string[] p = s.Split('|');
                if (p.Length < 2) continue;
                DateTime a, b;
                if (DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out a) &&
                    DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out b) &&
                    b > a)
                    list.Add(new Segment(a, b));
            }
            return list;
        }

        public static void SaveSegments(List<Segment> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Segment g in list)
                sb.AppendLine(g.Start.ToString("s") + "|" + g.End.ToString("s"));
            string tmp = SessionsFile + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(SessionsFile)) File.Delete(SessionsFile);
            File.Move(tmp, SessionsFile);
        }

        public static Dictionary<DateTime, double> LoadOverrides()
        {
            Dictionary<DateTime, double> map = new Dictionary<DateTime, double>();
            if (!File.Exists(OverridesFile)) return map;
            foreach (string line in File.ReadAllLines(OverridesFile))
            {
                string s = line.Trim();
                if (s.Length == 0) continue;
                string[] p = s.Split('|');
                if (p.Length < 2) continue;
                DateTime d; double m;
                if (DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out d) &&
                    double.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out m))
                    map[d.Date] = m;
            }
            return map;
        }

        public static void SaveOverrides(Dictionary<DateTime, double> map)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<DateTime, double> kv in map)
                sb.AppendLine(kv.Key.ToString("yyyy-MM-dd") + "|" +
                    kv.Value.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(OverridesFile, sb.ToString(), Encoding.UTF8);
        }
    }

    // ================= ОКНО =================
    class TrayApp : Form
    {
        List<Segment> segments = new List<Segment>();
        Dictionary<DateTime, double> overrides = new Dictionary<DateTime, double>();
        DateTime? runStart = null;
        double sessionAccumMin = 0;
        DateTime viewMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        DateTime lastSave = DateTime.Now;
        int lastSecond = -1;
        float pulse = 0;

        NotifyIcon tray;
        ContextMenuStrip menu;
        ToolStripMenuItem miStartPause, miStop, miShow, miAutostart, miWidget;
        Icon icoRun, icoIdle, icoPause;

        GButton btStartPause, btStop, btPrev, btNext, btExport, btMin, btClose, btWidget;
        DayList dayList;
        HudWidget hud;
        Dictionary<string, string> settings = new Dictionary<string, string>();
        System.Windows.Forms.Timer timer;

        string monthTitle = "", monthTotal = "0:00", statsLine = "";

        const int W = 486, H = 736;
        static readonly Rectangle HeroRect = new Rectangle(20, 62, W - 40, 154);
        static readonly Rectangle MonthRect = new Rectangle(20, 296, W - 40, 348);

        const string RUNKEY = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string RUNVAL = "WorkTimer";

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, int a, int b);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int wp, int lp);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

        public TrayApp()
        {
            segments = Store.LoadSegments();
            overrides = Store.LoadOverrides();
            settings = Store.LoadSettings();
            BuildIcons();
            BuildTray();
            BuildWindow();

            string wOn;
            if (!settings.TryGetValue("widget", out wOn) || wOn != "0") ShowHud();
            SyncWidgetUi();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += OnTick;
            timer.Start();

            UpdateAll();
            TrimMemory();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        static void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(
                    System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
        }

        // ---------- иконки трея ----------
        Icon MakeIcon(Color c1, Color c2, int glyph)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (GraphicsPath p = Skin.Round(new RectangleF(1, 1, 30, 30), 9))
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    new Rectangle(0, 0, 32, 32), c1, c2, LinearGradientMode.ForwardDiagonal))
                    g.FillPath(lg, p);
                using (SolidBrush w = new SolidBrush(Color.White))
                {
                    if (glyph == 1)
                    {
                        g.FillRectangle(w, 11f, 10f, 3.5f, 12f);
                        g.FillRectangle(w, 18f, 10f, 3.5f, 12f);
                    }
                    else if (glyph == 2)
                    {
                        using (GraphicsPath sp = Skin.Round(new RectangleF(11, 11, 10, 10), 2))
                            g.FillPath(w, sp);
                    }
                    else
                    {
                        g.FillPolygon(w, new PointF[] {
                            new PointF(12.5f, 9f), new PointF(23f, 16f), new PointF(12.5f, 23f) });
                    }
                }
                IntPtr h = bmp.GetHicon();
                Icon tmp = Icon.FromHandle(h);
                Icon clone = (Icon)tmp.Clone();
                DestroyIcon(h);
                return clone;
            }
        }

        void BuildIcons()
        {
            icoRun = MakeIcon(Skin.A1, Skin.Green, 0);
            icoPause = MakeIcon(Color.FromArgb(250, 176, 60), Color.FromArgb(240, 120, 60), 1);
            icoIdle = MakeIcon(Color.FromArgb(70, 80, 104), Color.FromArgb(45, 53, 72), 2);
        }

        // ---------- трей ----------
        void BuildTray()
        {
            menu = new ContextMenuStrip();
            menu.RenderMode = ToolStripRenderMode.Professional;
            menu.Renderer = new DarkRenderer();
            menu.BackColor = Skin.Card;
            menu.ForeColor = Skin.Text;
            menu.Font = Skin.F(9.5f, FontStyle.Regular);
            menu.ShowImageMargin = false;

            miStartPause = new ToolStripMenuItem("Старт", null, delegate { ToggleRun(); });
            miStop = new ToolStripMenuItem("Стоп — завершить сессию", null, delegate { StopSession(); });
            miShow = new ToolStripMenuItem("Показать окно", null, delegate { ToggleWindow(); });
            miAutostart = new ToolStripMenuItem("Запускать с Windows", null, delegate { ToggleAutostart(); });
            miWidget = new ToolStripMenuItem("Виджет на экране", null, delegate { ToggleWidget(); });

            menu.Items.Add(miStartPause);
            menu.Items.Add(miStop);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miShow);
            menu.Items.Add(new ToolStripMenuItem("Папка с данными", null, delegate {
                try { System.Diagnostics.Process.Start("explorer.exe", Store.Dir); } catch { } }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miWidget);
            menu.Items.Add(miAutostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, delegate { ExitApp(); }));

            tray = new NotifyIcon();
            tray.Icon = icoIdle;
            tray.Visible = true;
            tray.ContextMenuStrip = menu;
            tray.Text = "WorkTimer";
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ToggleWindow();
            };
            miAutostart.Checked = IsAutostart();
        }

        // ---------- окно ----------
        void BuildWindow()
        {
            Text = "WorkTimer — учёт рабочих часов";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            ShowInTaskbar = false;
            Icon = icoIdle;
            BackColor = Skin.Bg;
            ForeColor = Skin.Text;
            Font = Skin.F(9.5f, FontStyle.Regular);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            IntPtr rgn = CreateRoundRectRgn(0, 0, W + 1, H + 1, 20, 20);
            Region = Region.FromHrgn(rgn);
            DeleteObject(rgn);

            btMin = new GButton();
            btMin.Style = GButton.Kind.Icon;
            btMin.Text = "—";
            btMin.Font = Skin.F(10f, FontStyle.Regular);
            btMin.ForeColor = Skin.Muted;
            btMin.Radius = 8;
            btMin.SetBounds(W - 78, 14, 28, 28);
            btMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(btMin);

            btClose = new GButton();
            btClose.Style = GButton.Kind.Icon;
            btClose.Text = "✕";
            btClose.Font = Skin.F(10f, FontStyle.Regular);
            btClose.ForeColor = Skin.Muted;
            btClose.Danger = true;
            btClose.Radius = 8;
            btClose.SetBounds(W - 44, 14, 28, 28);
            btClose.Click += delegate { HideWindow(); };
            Controls.Add(btClose);

            btStartPause = new GButton();
            btStartPause.Style = GButton.Kind.Primary;
            btStartPause.Text = "СТАРТ";
            btStartPause.Font = Skin.F(10.5f, FontStyle.Bold);
            btStartPause.Radius = 14;
            btStartPause.SetBounds(20, 232, 268, 52);
            btStartPause.Click += delegate { ToggleRun(); };
            Controls.Add(btStartPause);

            btStop = new GButton();
            btStop.Text = "Стоп";
            btStop.Font = Skin.F(10f, FontStyle.Regular);
            btStop.ForeColor = Skin.Muted;
            btStop.Radius = 14;
            btStop.SetBounds(298, 232, W - 318, 52);
            btStop.Click += delegate { StopSession(); };
            Controls.Add(btStop);

            btPrev = new GButton();
            btPrev.Style = GButton.Kind.Icon;
            btPrev.Text = "‹";
            btPrev.Font = Skin.F(14f, FontStyle.Bold);
            btPrev.ForeColor = Skin.Muted;
            btPrev.ParentBg = Skin.Card;
            btPrev.Radius = 9;
            btPrev.SetBounds(MonthRect.X + 14, MonthRect.Y + 16, 30, 30);
            btPrev.Click += delegate { viewMonth = viewMonth.AddMonths(-1); UpdateAll(); };
            Controls.Add(btPrev);

            btNext = new GButton();
            btNext.Style = GButton.Kind.Icon;
            btNext.Text = "›";
            btNext.Font = Skin.F(14f, FontStyle.Bold);
            btNext.ForeColor = Skin.Muted;
            btNext.ParentBg = Skin.Card;
            btNext.Radius = 9;
            btNext.SetBounds(MonthRect.X + 48, MonthRect.Y + 16, 30, 30);
            btNext.Click += delegate { viewMonth = viewMonth.AddMonths(1); UpdateAll(); };
            Controls.Add(btNext);

            dayList = new DayList();
            dayList.BackColor = Skin.Card;
            dayList.SetBounds(MonthRect.X + 8, MonthRect.Y + 96, MonthRect.Width - 16, 240);
            dayList.DayActivated = EditDay;
            Controls.Add(dayList);

            btExport = new GButton();
            btExport.Text = "Экспорт CSV";
            btExport.Radius = 12;
            btExport.ForeColor = Skin.Muted;
            btExport.SetBounds(20, H - 52, 132, 38);
            btExport.Click += delegate { ExportCsv(); };
            Controls.Add(btExport);

            btWidget = new GButton();
            btWidget.Text = "Виджет: выкл";
            btWidget.Radius = 12;
            btWidget.ForeColor = Skin.Muted;
            btWidget.SetBounds(162, H - 52, 128, 38);
            btWidget.Click += delegate { ToggleWidget(); };
            Controls.Add(btWidget);

            GButton btHide = new GButton();
            btHide.Text = "Свернуть в трей";
            btHide.Radius = 12;
            btHide.ForeColor = Skin.Muted;
            btHide.SetBounds(300, H - 52, 166, 38);
            btHide.Click += delegate { HideWindow(); };
            Controls.Add(btHide);

            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < 56)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 2, 0);
                }
            };
        }

        // ---------- отрисовка ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bg = new SolidBrush(Skin.Bg)) g.FillRectangle(bg, ClientRectangle);

            bool running = Running;
            bool paused = !running && sessionAccumMin > 0;

            // шапка
            using (GraphicsPath lp = Skin.Round(new RectangleF(20, 21, 14, 14), 4))
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new Rectangle(20, 21, 14, 14), Skin.A1, Skin.A2, LinearGradientMode.ForwardDiagonal))
                g.FillPath(lg, lp);
            TextRenderer.DrawText(g, "W O R K T I M E R", Skin.F(8.5f, FontStyle.Bold),
                new Rectangle(44, 20, 240, 18), Skin.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // карточка «сегодня»
            Color acc = running ? Skin.Green : (paused ? Skin.Amber : Skin.Dim);
            using (GraphicsPath hp = Skin.Round(HeroRect, 20))
            {
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    HeroRect, Color.FromArgb(26, 31, 45), Color.FromArgb(19, 23, 33),
                    LinearGradientMode.Vertical))
                    g.FillPath(lg, hp);

                if (running)
                {
                    int a = (int)(40 + 45 * (1 + Math.Sin(pulse)) / 2);
                    using (Pen glow = new Pen(Color.FromArgb(a, Skin.Green), 2f)) g.DrawPath(glow, hp);
                }
                else
                    using (Pen pn = new Pen(Skin.Line, 1f)) g.DrawPath(pn, hp);
            }

            using (GraphicsPath sp = Skin.Round(new RectangleF(HeroRect.X + 1, HeroRect.Y + 26, 4, 44), 2))
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new RectangleF(HeroRect.X, HeroRect.Y + 24, 4, 48),
                running ? Skin.Green : Skin.A1, Skin.A2, LinearGradientMode.Vertical))
                g.FillPath(lg, sp);

            string st = running ? "ИДЁТ УЧЁТ" : (paused ? "ПАУЗА" : "ОСТАНОВЛЕН");
            Size stSz = TextRenderer.MeasureText(g, st, Skin.F(8f, FontStyle.Bold),
                Size.Empty, TextFormatFlags.NoPadding);
            RectangleF pill = new RectangleF(HeroRect.X + 22, HeroRect.Y + 20, stSz.Width + 36, 22);
            using (GraphicsPath pp = Skin.Round(pill, 11))
            using (SolidBrush pb = new SolidBrush(Color.FromArgb(38, acc)))
                g.FillPath(pb, pp);
            using (SolidBrush db = new SolidBrush(acc))
                g.FillEllipse(db, pill.X + 11, pill.Y + 8.5f, 6, 6);
            TextRenderer.DrawText(g, st, Skin.F(8f, FontStyle.Bold),
                new Rectangle((int)pill.X + 23, (int)pill.Y, (int)pill.Width, (int)pill.Height), acc,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            string big = Fmt(TodayMinutes());
            using (Font bf = new Font("Segoe UI", 42f, FontStyle.Bold))
            {
                Size bs = TextRenderer.MeasureText(g, big, bf, Size.Empty, TextFormatFlags.NoPadding);
                Rectangle br = new Rectangle(HeroRect.X + 20, HeroRect.Y + 50, bs.Width + 10, 66);
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    br, Skin.Text, running ? Skin.A2 : Skin.Muted, LinearGradientMode.Horizontal))
                    g.DrawString(big, bf, lg, br.X, br.Y, StringFormat.GenericTypographic);
            }
            TextRenderer.DrawText(g, "часов сегодня", Skin.F(8.5f, FontStyle.Regular),
                new Rectangle(HeroRect.X + 24, HeroRect.Y + 120, 200, 18), Skin.Dim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, "ТЕКУЩАЯ СЕССИЯ", Skin.F(7.5f, FontStyle.Bold),
                new Rectangle(HeroRect.Right - 210, HeroRect.Y + 62, 190, 16), Skin.Dim,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Fmt(CurrentSessionMinutes()), Skin.F(18f, FontStyle.Bold),
                new Rectangle(HeroRect.Right - 210, HeroRect.Y + 80, 190, 30),
                running ? Skin.Text : Skin.Muted,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);

            // карточка месяца
            using (GraphicsPath mp = Skin.Round(MonthRect, 20))
            {
                using (SolidBrush mb = new SolidBrush(Skin.Card)) g.FillPath(mb, mp);
                using (Pen pn = new Pen(Skin.Line, 1f)) g.DrawPath(pn, mp);
            }

            TextRenderer.DrawText(g, monthTitle, Skin.F(11f, FontStyle.Bold),
                new Rectangle(MonthRect.X + 88, MonthRect.Y + 18, 200, 26), Skin.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, "ИТОГО ЗА МЕСЯЦ", Skin.F(7.5f, FontStyle.Bold),
                new Rectangle(MonthRect.Right - 200, MonthRect.Y + 14, 186, 14), Skin.Dim,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
            using (Font tf = new Font("Segoe UI", 17f, FontStyle.Bold))
            {
                Size ts = TextRenderer.MeasureText(g, monthTotal, tf, Size.Empty, TextFormatFlags.NoPadding);
                Rectangle tr = new Rectangle(MonthRect.Right - 16 - ts.Width, MonthRect.Y + 28, ts.Width + 6, 28);
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    tr, Skin.A2, Skin.A1, LinearGradientMode.Horizontal))
                    g.DrawString(monthTotal, tf, lg, tr.X, tr.Y, StringFormat.GenericTypographic);
            }

            using (Pen pn = new Pen(Color.FromArgb(32, 38, 54), 1f))
                g.DrawLine(pn, MonthRect.X + 14, MonthRect.Y + 84, MonthRect.Right - 14, MonthRect.Y + 84);

            TextRenderer.DrawText(g, statsLine, Skin.F(8.5f, FontStyle.Regular),
                new Rectangle(22, MonthRect.Bottom + 10, W - 44, 18), Skin.Dim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "2 клика по дню — правка", Skin.F(8f, FontStyle.Regular),
                new Rectangle(22, MonthRect.Bottom + 10, W - 44, 18), Color.FromArgb(70, 79, 100),
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated) { CreateHandle(); value = false; }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideWindow();
                return;
            }
            SaveNow();
            base.OnFormClosing(e);
        }

        void ToggleWindow()
        {
            if (Visible && WindowState != FormWindowState.Minimized) HideWindow();
            else
            {
                Show();
                ShowInTaskbar = true;
                WindowState = FormWindowState.Normal;
                timer.Interval = 50;
                UpdateAll();
                Activate();
                BringToFront();
            }
        }

        void HideWindow()
        {
            Hide();
            ShowInTaskbar = false;
            timer.Interval = 1000;
            UpdateTrayText();
            TrimMemory();
        }

        // ---------- логика ----------
        bool Running { get { return runStart.HasValue; } }

        // --- доступ для плавающего виджета ---
        internal bool RunningPub { get { return Running; } }
        internal double TodayPub { get { return TodayMinutes(); } }
        internal double SessionPub { get { return CurrentSessionMinutes(); } }
        internal void TogglePub() { ToggleRun(); }
        internal void OpenMainPub() { if (!Visible) ToggleWindow(); else { Activate(); BringToFront(); } }
        internal void ShowMenuAt(Point screenPt) { menu.Show(screenPt); }

        internal void SaveWidgetPos()
        {
            if (hud == null || hud.IsDisposed) return;
            settings["wx"] = hud.Left.ToString(CultureInfo.InvariantCulture);
            settings["wy"] = hud.Top.ToString(CultureInfo.InvariantCulture);
            Store.SaveSettings(settings);
        }

        void ToggleWidget()
        {
            bool turnOn = hud == null || hud.IsDisposed;
            settings["widget"] = turnOn ? "1" : "0";
            Store.SaveSettings(settings);
            if (turnOn) ShowHud(); else HideHud();
            SyncWidgetUi();
        }

        void ShowHud()
        {
            if (hud != null && !hud.IsDisposed) return;
            hud = new HudWidget(this);

            int x, y;
            string sx, sy;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (!(settings.TryGetValue("wx", out sx) && settings.TryGetValue("wy", out sy) &&
                  int.TryParse(sx, NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
                  int.TryParse(sy, NumberStyles.Integer, CultureInfo.InvariantCulture, out y)))
            {
                x = wa.Right - hud.Width - 24;
                y = wa.Top + 24;
            }

            Rectangle virt = SystemInformation.VirtualScreen;
            if (x < virt.Left) x = virt.Left + 24;
            if (y < virt.Top) y = virt.Top + 24;
            if (x > virt.Right - 60) x = virt.Right - hud.Width - 24;
            if (y > virt.Bottom - 40) y = virt.Bottom - hud.Height - 24;

            hud.Location = new Point(x, y);
            hud.Show();
            hud.Refresh2();
        }

        void HideHud()
        {
            if (hud == null) return;
            if (!hud.IsDisposed) { hud.Hide(); hud.Dispose(); }
            hud = null;
        }

        void SyncWidgetUi()
        {
            bool on = hud != null && !hud.IsDisposed;
            miWidget.Checked = on;
            btWidget.Text = on ? "Виджет: вкл" : "Виджет: выкл";
            btWidget.ForeColor = on ? Skin.A2 : Skin.Muted;
            btWidget.Invalidate();
        }

        void ToggleRun()
        {
            if (Running) CloseSegment();
            else runStart = DateTime.Now;
            SaveNow();
            UpdateAll();
        }

        void StopSession()
        {
            if (Running) CloseSegment();
            sessionAccumMin = 0;
            SaveNow();
            UpdateAll();
        }

        void CloseSegment()
        {
            if (!runStart.HasValue) return;
            DateTime s = runStart.Value;
            DateTime e = DateTime.Now;
            if ((e - s).TotalSeconds >= 1)
            {
                segments.Add(new Segment(s, e));
                sessionAccumMin += (e - s).TotalMinutes;
            }
            runStart = null;
        }

        void OnTick(object sender, EventArgs e)
        {
            if (Running && (DateTime.Now - lastSave).TotalSeconds >= 30) SaveNow();

            if (Visible)
            {
                if (Running) { pulse += 0.12f; Invalidate(HeroRect); }
                if (DateTime.Now.Second != lastSecond)
                {
                    lastSecond = DateTime.Now.Second;
                    UpdateTrayText();
                    UpdateWindow();
                }
            }
            else UpdateTrayText();
        }

        void SaveNow()
        {
            try
            {
                List<Segment> all = new List<Segment>(segments);
                if (Running && (DateTime.Now - runStart.Value).TotalSeconds >= 1)
                    all.Add(new Segment(runStart.Value, DateTime.Now));
                Store.SaveSegments(all);
                Store.SaveOverrides(overrides);
                lastSave = DateTime.Now;
            }
            catch { }
        }

        static void AddSpan(Dictionary<DateTime, double> map, DateTime s, DateTime e)
        {
            while (s < e)
            {
                DateTime dayEnd = s.Date.AddDays(1);
                DateTime chunk = e < dayEnd ? e : dayEnd;
                double m = (chunk - s).TotalMinutes;
                DateTime k = s.Date;
                if (map.ContainsKey(k)) map[k] = map[k] + m; else map[k] = m;
                s = chunk;
            }
        }

        Dictionary<DateTime, double> BuildDayMap()
        {
            Dictionary<DateTime, double> map = new Dictionary<DateTime, double>();
            foreach (Segment g in segments) AddSpan(map, g.Start, g.End);
            if (Running) AddSpan(map, runStart.Value, DateTime.Now);
            foreach (KeyValuePair<DateTime, double> kv in overrides) map[kv.Key] = kv.Value;
            return map;
        }

        public static string Fmt(double minutes)
        {
            if (minutes < 0) minutes = 0;
            int total = (int)Math.Round(minutes);
            return string.Format("{0}:{1:00}", total / 60, total % 60);
        }

        double TodayMinutes()
        {
            Dictionary<DateTime, double> map = BuildDayMap();
            DateTime k = DateTime.Now.Date;
            return map.ContainsKey(k) ? map[k] : 0;
        }

        double CurrentSessionMinutes()
        {
            double m = sessionAccumMin;
            if (Running) m += (DateTime.Now - runStart.Value).TotalMinutes;
            return m;
        }

        // ---------- обновление ----------
        void UpdateAll()
        {
            UpdateTrayText();
            UpdateWindow();
            if (hud != null && !hud.IsDisposed) hud.Refresh2();
        }

        void UpdateTrayText()
        {
            bool paused = !Running && sessionAccumMin > 0;
            string state = Running ? "Идёт учёт" : (paused ? "Пауза" : "Остановлен");
            tray.Icon = Running ? icoRun : (paused ? icoPause : icoIdle);
            string t = state + " • сегодня " + Fmt(TodayMinutes());
            if (t.Length > 62) t = t.Substring(0, 62);
            tray.Text = t;
            miStartPause.Text = Running ? "Пауза" : (paused ? "Продолжить" : "Старт");
            miShow.Text = Visible ? "Скрыть окно" : "Показать окно";
        }

        void UpdateWindow()
        {
            if (!Visible) return;
            bool running = Running;
            bool paused = !running && sessionAccumMin > 0;

            btStartPause.Text = running ? "ПАУЗА" : (paused ? "ПРОДОЛЖИТЬ" : "СТАРТ");
            if (running)
            {
                btStartPause.A = Color.FromArgb(250, 176, 60);
                btStartPause.B = Color.FromArgb(240, 110, 90);
            }
            else { btStartPause.A = Skin.A1; btStartPause.B = Skin.A2; }
            btStartPause.Invalidate();
            Icon = running ? icoRun : (paused ? icoPause : icoIdle);

            CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");
            monthTitle = ru.TextInfo.ToTitleCase(viewMonth.ToString("MMMM yyyy", ru));

            Dictionary<DateTime, double> map = BuildDayMap();
            int days = DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month);
            double total = 0, max = 480; int worked = 0;
            List<DayRow> rows = new List<DayRow>();

            for (int d = 1; d <= days; d++)
            {
                DateTime day = new DateTime(viewMonth.Year, viewMonth.Month, d);
                double m = map.ContainsKey(day) ? map[day] : 0;
                if (m <= 0) continue;
                total += m; worked++;
                if (m > max) max = m;
                DayRow r = new DayRow();
                r.Day = day;
                r.Minutes = m;
                r.Manual = overrides.ContainsKey(day);
                r.IsToday = day.Date == DateTime.Now.Date;
                r.Weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                rows.Add(r);
            }

            dayList.SetRows(rows, max);
            monthTotal = Fmt(total);
            statsLine = string.Format("{0} дн.   ·   в среднем {1}   ·   {2} ч. десятичных",
                worked, Fmt(worked > 0 ? total / worked : 0),
                (total / 60.0).ToString("0.00", CultureInfo.InvariantCulture));

            Invalidate();
        }

        // ---------- правка / экспорт ----------
        void EditDay(DateTime day)
        {
            Dictionary<DateTime, double> map = BuildDayMap();
            double cur = map.ContainsKey(day) ? map[day] : 0;
            string res = Prompt.Show(this, day.ToString("dd.MM.yyyy"),
                "Часы за день: 7:30, 7.5 или 450m.  Пустое поле — вернуть автоподсчёт.", Fmt(cur));
            if (res == null) return;
            res = res.Trim();
            if (res.Length == 0)
            {
                if (overrides.ContainsKey(day)) overrides.Remove(day);
            }
            else
            {
                double mins;
                if (!ParseHours(res, out mins))
                {
                    MessageBox.Show("Не понял формат. Примеры: 7:30, 7.5, 450m", "WorkTimer");
                    return;
                }
                overrides[day] = mins;
            }
            SaveNow();
            UpdateAll();
        }

        static bool ParseHours(string s, out double minutes)
        {
            minutes = 0;
            s = s.Trim().Replace(',', '.');
            if (s.Length == 0) return false;
            if (s.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            {
                double mm;
                if (double.TryParse(s.Substring(0, s.Length - 1), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out mm)) { minutes = mm; return true; }
                return false;
            }
            if (s.IndexOf(':') >= 0)
            {
                string[] p = s.Split(':');
                int h, m;
                if (p.Length == 2 && int.TryParse(p[0], out h) && int.TryParse(p[1], out m))
                { minutes = h * 60 + m; return true; }
                return false;
            }
            double hrs;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out hrs))
            { minutes = hrs * 60; return true; }
            return false;
        }

        void ExportCsv()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "CSV|*.csv";
            dlg.FileName = string.Format("worktime_{0:yyyy-MM}.csv", viewMonth);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            Dictionary<DateTime, double> map = BuildDayMap();
            int days = DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month);
            CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Дата;День недели;Часы (чч:мм);Часы (десятичные)");
            double total = 0;
            for (int d = 1; d <= days; d++)
            {
                DateTime day = new DateTime(viewMonth.Year, viewMonth.Month, d);
                double m = map.ContainsKey(day) ? map[day] : 0;
                if (m <= 0) continue;
                total += m;
                sb.AppendLine(string.Format("{0};{1};{2};{3}",
                    day.ToString("dd.MM.yyyy"), day.ToString("ddd", ru), Fmt(m),
                    (m / 60.0).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')));
            }
            sb.AppendLine();
            sb.AppendLine(string.Format("ИТОГО;;{0};{1}", Fmt(total),
                (total / 60.0).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')));
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show("Сохранено: " + dlg.FileName, "WorkTimer");
        }

        // ---------- автозапуск ----------
        bool IsAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUNKEY, false))
                {
                    if (k == null) return false;
                    return k.GetValue(RUNVAL) != null;
                }
            }
            catch { return false; }
        }

        void ToggleAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUNKEY, true))
                {
                    if (k == null) return;
                    if (IsAutostart()) k.DeleteValue(RUNVAL, false);
                    else k.SetValue(RUNVAL, "\"" + Application.ExecutablePath + "\"");
                }
                miAutostart.Checked = IsAutostart();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось изменить автозапуск: " + ex.Message, "WorkTimer");
            }
        }

        void ExitApp()
        {
            if (Running) CloseSegment();
            SaveNow();
            SaveWidgetPos();
            HideHud();
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }
    }

    // ================= ПЛАВАЮЩИЙ ВИДЖЕТ =================
    class HudWidget : Form
    {
        readonly TrayApp app;
        const int WW = 214, WH = 70;

        bool hover, overBtn, pressBtn, dragging, armed;
        Point grabScreen, grabOrigin;
        Bitmap buf;        // переиспользуемый холст — чтобы не мусорить каждым кадром
        Graphics bufG;
        float pulse = 0;
        string lastKey = "";
        System.Windows.Forms.Timer tick;

        static readonly Rectangle BtnRect = new Rectangle(WW - 52, WH / 2 - 17, 34, 34);

        public HudWidget(TrayApp owner)
        {
            app = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(WW, WH);
            Cursor = Cursors.SizeAll;

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 1000;
            tick.Tick += delegate { Beat(); };
            tick.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;  // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080;  // WS_EX_TOOLWINDOW — не показывать в Alt+Tab
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Render();
        }

        void Beat()
        {
            bool run = app.RunningPub;
            tick.Interval = run ? 120 : 1000;
            if (run) pulse += 0.14f;
            string key = Key();
            if (run || key != lastKey || hover) { lastKey = key; Render(); }
        }

        string Key()
        {
            return app.RunningPub + "|" + TrayApp.Fmt(app.TodayPub) + "|" +
                   TrayApp.Fmt(app.SessionPub) + "|" + hover + overBtn;
        }

        public void Refresh2() { lastKey = ""; Beat(); }

        // ---------- отрисовка в ARGB-битмап ----------
        void Render()
        {
            if (!IsHandleCreated) return;
            if (buf == null)
            {
                buf = new Bitmap(WW, WH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                bufG = Graphics.FromImage(buf);
            }
            {
                Graphics g = bufG;
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    bool run = app.RunningPub;
                    bool paused = !run && app.SessionPub > 0;
                    Color acc = run ? Skin.Green : (paused ? Skin.Amber : Skin.Muted);
                    int bgA = hover ? 246 : 216;

                    RectangleF card = new RectangleF(1, 1, WW - 2, WH - 2);
                    using (GraphicsPath p = Skin.Round(card, 18))
                    {
                        using (LinearGradientBrush lg = new LinearGradientBrush(card,
                            Color.FromArgb(bgA, 24, 28, 40), Color.FromArgb(bgA, 14, 17, 25),
                            LinearGradientMode.Vertical))
                            g.FillPath(lg, p);
                        using (Pen pn = new Pen(Color.FromArgb(run ? 90 : 46, acc), 1.4f))
                            g.DrawPath(pn, p);
                    }

                    // пульсирующая точка состояния
                    float k = run ? (float)(0.5 + 0.5 * Math.Sin(pulse)) : 1f;
                    float dr = run ? 5f + 2f * k : 5f;
                    if (run)
                        using (SolidBrush halo = new SolidBrush(Color.FromArgb((int)(70 * k), acc)))
                            g.FillEllipse(halo, 17 - dr - 4, WH / 2f - dr - 4, (dr + 4) * 2, (dr + 4) * 2);
                    using (SolidBrush db = new SolidBrush(acc))
                        g.FillEllipse(db, 17 - 4.5f, WH / 2f - 4.5f, 9, 9);

                    // время
                    string big = TrayApp.Fmt(app.TodayPub);
                    using (Font bf = new Font("Segoe UI", 19f, FontStyle.Bold))
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new RectangleF(32, 10, 110, 30), Color.FromArgb(250, 245, 248, 255),
                        run ? Color.FromArgb(250, Skin.A2) : Color.FromArgb(230, Skin.Muted),
                        LinearGradientMode.Horizontal))
                        g.DrawString(big, bf, lg, 31, 11, StringFormat.GenericTypographic);

                    string sub = run || paused
                        ? "сессия " + TrayApp.Fmt(app.SessionPub)
                        : "остановлен";
                    using (Font sf = new Font("Segoe UI", 7.5f, FontStyle.Regular))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(190, Skin.Dim)))
                        g.DrawString(sub, sf, sb, 33, 43, StringFormat.GenericTypographic);

                    // кнопка старт/пауза
                    Color bc = overBtn
                        ? (pressBtn ? Color.FromArgb(255, Skin.Card3) : Color.FromArgb(240, Skin.Card3))
                        : Color.FromArgb(150, Skin.Card2);
                    using (SolidBrush bb = new SolidBrush(bc))
                        g.FillEllipse(bb, BtnRect);
                    if (overBtn)
                        using (Pen pn = new Pen(Color.FromArgb(120, acc), 1.2f))
                            g.DrawEllipse(pn, BtnRect);

                    float cx = BtnRect.X + BtnRect.Width / 2f, cy = BtnRect.Y + BtnRect.Height / 2f;
                    using (SolidBrush w = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                    {
                        if (run)
                        {
                            g.FillRectangle(w, cx - 4.5f, cy - 6, 3.2f, 12);
                            g.FillRectangle(w, cx + 1.3f, cy - 6, 3.2f, 12);
                        }
                        else
                        {
                            g.FillPolygon(w, new PointF[] {
                                new PointF(cx - 4, cy - 6.5f),
                                new PointF(cx + 6, cy),
                                new PointF(cx - 4, cy + 6.5f) });
                        }
                    }

                    // прогресс до 8 часов
                    float frac = (float)Math.Min(1.0, app.TodayPub / 480.0);
                    RectangleF tr = new RectangleF(16, WH - 11, WW - 32, 3);
                    using (GraphicsPath tp = Skin.Round(tr, 1.5f))
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(70, 90, 105, 140)))
                        g.FillPath(tb, tp);
                    if (frac > 0.005f)
                    {
                        RectangleF fr = new RectangleF(tr.X, tr.Y, tr.Width * frac, 3);
                        using (GraphicsPath fp = Skin.Round(fr, 1.5f))
                        using (LinearGradientBrush lg = new LinearGradientBrush(
                            new RectangleF(tr.X, tr.Y, tr.Width, 3),
                            frac >= 1f ? Skin.Green : Skin.A1,
                            frac >= 1f ? Skin.A2 : Skin.A2, LinearGradientMode.Horizontal))
                            g.FillPath(lg, fp);
                    }
                }
                SetBitmap(buf);
            }
        }

        // ---------- слоёное окно ----------
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst,
            ref PT ppt, ref SZ psz, IntPtr src, ref PT pptSrc, int key, ref BLEND bl, int flags);

        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential)]
        struct BLEND { public byte Op, Flags, Alpha, Format; }

        void SetBitmap(Bitmap bmp)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hbmp);
                SZ sz; sz.W = bmp.Width; sz.H = bmp.Height;
                PT src; src.X = 0; src.Y = 0;
                PT pos; pos.X = Left; pos.Y = Top;
                BLEND bl;
                bl.Op = 0; bl.Flags = 0; bl.Alpha = 255; bl.Format = 1; // AC_SRC_ALPHA
                UpdateLayeredWindow(Handle, screen, ref pos, ref sz, mem, ref src, 0, ref bl, 2);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
                if (hbmp != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hbmp); }
                DeleteDC(mem);
            }
        }

        // ---------- мышь ----------
        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true; Render(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false; overBtn = false; pressBtn = false; Render(); base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (armed)
            {
                Point now = PointToScreen(e.Location);
                int dx = now.X - grabScreen.X, dy = now.Y - grabScreen.Y;
                if (!dragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)) dragging = true;
                if (dragging) Location = new Point(grabOrigin.X + dx, grabOrigin.Y + dy);
                return;
            }
            bool ob = BtnRect.Contains(e.Location);
            Cursor = ob ? Cursors.Hand : Cursors.SizeAll;
            if (ob != overBtn) { overBtn = ob; Render(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                app.ShowMenuAt(PointToScreen(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (BtnRect.Contains(e.Location)) { pressBtn = true; Render(); return; }

            armed = true;
            dragging = false;
            grabScreen = PointToScreen(e.Location);
            grabOrigin = Location;
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (pressBtn)
            {
                pressBtn = false;
                if (BtnRect.Contains(e.Location)) app.TogglePub();
                Render();
            }
            if (armed)
            {
                armed = false;
                Capture = false;
                if (dragging) { dragging = false; app.SaveWidgetPos(); }
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (!BtnRect.Contains(e.Location)) app.OpenMainPub();
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (IsHandleCreated) Render();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (tick != null) { tick.Stop(); tick.Dispose(); tick = null; }
                if (bufG != null) { bufG.Dispose(); bufG = null; }
                if (buf != null) { buf.Dispose(); buf = null; }
            }
            base.Dispose(disposing);
        }
    }

    // ================= ТЁМНОЕ МЕНЮ =================
    class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Skin.Card; } }
        public override Color ImageMarginGradientBegin { get { return Skin.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Skin.Card; } }
        public override Color ImageMarginGradientEnd { get { return Skin.Card; } }
        public override Color MenuItemSelected { get { return Skin.Card3; } }
        public override Color MenuItemSelectedGradientBegin { get { return Skin.Card3; } }
        public override Color MenuItemSelectedGradientEnd { get { return Skin.Card3; } }
        public override Color MenuItemBorder { get { return Skin.Card3; } }
        public override Color MenuBorder { get { return Skin.Line; } }
        public override Color SeparatorDark { get { return Skin.Line; } }
        public override Color SeparatorLight { get { return Skin.Card; } }
        public override Color CheckBackground { get { return Skin.A1; } }
        public override Color CheckSelectedBackground { get { return Skin.A1; } }
        public override Color CheckPressedBackground { get { return Skin.A1; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColorTable()) { RoundedEdges = true; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? (e.Item.Selected ? Color.White : Skin.Text)
                : Skin.Dim;
            base.OnRenderItemText(e);
        }
    }

    // ================= ДИАЛОГ ПРАВКИ =================
    static class Prompt
    {
        public static string Show(Form owner, string title, string text, string def)
        {
            using (Form f = new Form())
            {
                f.FormBorderStyle = FormBorderStyle.None;
                f.ClientSize = new Size(340, 176);
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Skin.Card;
                f.Font = Skin.F(9.5f, FontStyle.Regular);
                f.ShowInTaskbar = false;

                f.Paint += delegate(object s, PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Pen p = new Pen(Skin.Card3, 1f))
                        g.DrawRectangle(p, 0, 0, f.ClientSize.Width - 1, f.ClientSize.Height - 1);
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new Rectangle(0, 0, f.ClientSize.Width, 3), Skin.A1, Skin.A2,
                        LinearGradientMode.Horizontal))
                        g.FillRectangle(lg, 0, 0, f.ClientSize.Width, 3);
                    TextRenderer.DrawText(g, title, Skin.F(13f, FontStyle.Bold),
                        new Rectangle(20, 18, 300, 28), Skin.Text,
                        TextFormatFlags.Left | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, text, Skin.F(8.5f, FontStyle.Regular),
                        new Rectangle(20, 48, 300, 40), Skin.Muted,
                        TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
                };

                TextBox tb = new TextBox();
                tb.Text = def;
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = Skin.Card2;
                tb.ForeColor = Skin.Text;
                tb.Font = Skin.F(13f, FontStyle.Bold);
                tb.SetBounds(20, 96, 300, 32);
                f.Controls.Add(tb);

                GButton ok = new GButton();
                ok.Style = GButton.Kind.Primary;
                ok.Text = "Сохранить";
                ok.Font = Skin.F(9.5f, FontStyle.Bold);
                ok.ParentBg = Skin.Card;
                ok.SetBounds(180, 138, 140, 32);
                ok.Click += delegate { f.DialogResult = DialogResult.OK; f.Close(); };
                f.Controls.Add(ok);

                GButton ca = new GButton();
                ca.Text = "Отмена";
                ca.ForeColor = Skin.Muted;
                ca.ParentBg = Skin.Card;
                ca.SetBounds(20, 138, 110, 32);
                ca.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };
                f.Controls.Add(ca);

                f.KeyPreview = true;
                f.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.OK; f.Close(); }
                    if (e.KeyCode == Keys.Escape) { f.DialogResult = DialogResult.Cancel; f.Close(); }
                };

                tb.SelectAll();
                DialogResult r = f.ShowDialog(owner);
                return r == DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
