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

        // шрифты живут всё время работы программы: перерисовка идёт до 20 раз
        // в секунду, создавать их каждый кадр — лишняя нагрузка на GDI
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static Font F(float size, FontStyle st)
        {
            string k = size.ToString("0.##", CultureInfo.InvariantCulture) + "/" + (int)st;
            Font f;
            if (!fonts.TryGetValue(k, out f))
            {
                f = new Font("Segoe UI", size, st);
                fonts[k] = f;
            }
            return f;
        }

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

        // плавное затухание к концу — для всех анимаций
        public static float EaseOut(float t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            float u = 1 - t;
            return 1 - u * u * u;
        }

        // шаг к цели; возвращает false в done, пока не доехали
        public static float Approach(float cur, float target, float k, ref bool done)
        {
            float d = target - cur;
            if (Math.Abs(d) < 0.006f) return target;
            done = false;
            return cur + d * k;
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
        float hoverT, pressT;                    // сглаженные состояния 0..1
        System.Windows.Forms.Timer anim;

        public GButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            ForeColor = Skin.Text;
            Font = Skin.F(9.5f, FontStyle.Regular);
        }

        void Animate()
        {
            if (anim == null)
            {
                anim = new System.Windows.Forms.Timer();
                anim.Interval = 16;
                anim.Tick += delegate
                {
                    bool done = true;
                    hoverT = Skin.Approach(hoverT, hover ? 1f : 0f, 0.24f, ref done);
                    pressT = Skin.Approach(pressT, press ? 1f : 0f, 0.40f, ref done);
                    Invalidate();
                    if (done) anim.Stop();
                };
            }
            anim.Start();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Animate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; Animate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { press = true; Animate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { press = false; Animate(); base.OnMouseUp(e); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && anim != null) { anim.Stop(); anim.Dispose(); anim = null; }
            base.Dispose(disposing);
        }

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
                    Color ca = Skin.Mix(Skin.Mix(A, Color.White, 0.14f * hoverT), Color.Black, 0.20f * pressT);
                    Color cb = Skin.Mix(Skin.Mix(B, Color.White, 0.14f * hoverT), Color.Black, 0.20f * pressT);
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new Rectangle(0, 0, Width, Height), ca, cb, LinearGradientMode.Horizontal))
                        g.FillPath(lg, p);
                    if (hoverT > 0.01f)
                        using (Pen gl = new Pen(Color.FromArgb((int)(110 * hoverT), Color.White), 1.2f))
                            g.DrawPath(gl, p);
                }
                else if (Style == Kind.Ghost)
                {
                    Color f = Skin.Mix(Skin.Mix(Color.FromArgb(26, 31, 44), Skin.Card2, hoverT),
                                       Skin.Card3, pressT);
                    using (SolidBrush sb = new SolidBrush(f)) g.FillPath(sb, p);
                    using (Pen pn = new Pen(Skin.Mix(Skin.Line, Skin.Card3, hoverT), 1f)) g.DrawPath(pn, p);
                }
                else
                {
                    if (hoverT > 0.01f)
                    {
                        Color target = Danger ? Color.FromArgb(220, 60, 80) : Skin.Card2;
                        using (SolidBrush sb = new SolidBrush(Skin.Mix(ParentBg, target, hoverT)))
                            g.FillPath(sb, p);
                    }
                }
            }

            Color tc = Style == Kind.Primary ? Color.White
                : Skin.Mix(ForeColor, Danger ? Color.White : Skin.Text, hoverT);
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

        float[] prog;                    // прогресс появления каждой строки
        DateTime animStart;
        string lastKey = "";
        System.Windows.Forms.Timer anim;

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
            // строки перерисовываются раз в секунду — анимируем только когда
            // состав списка сменился (переключили месяц, появился новый день)
            string key = rows.Count > 0
                ? rows[0].Day.ToString("yyyy-MM") + "/" + rows.Count
                : "empty";
            bool fresh = key != lastKey;
            lastKey = key;

            Rows = rows; BarScale = scale;
            if (scroll > MaxScroll) scroll = MaxScroll;

            if (fresh)
            {
                prog = new float[rows.Count];
                animStart = DateTime.Now;
                StartAnim();
            }
            else if (prog == null || prog.Length != rows.Count)
            {
                prog = new float[rows.Count];
                for (int i = 0; i < prog.Length; i++) prog[i] = 1f;
            }
            Invalidate();
        }

        void StartAnim()
        {
            if (anim == null)
            {
                anim = new System.Windows.Forms.Timer();
                anim.Interval = 16;
                anim.Tick += delegate
                {
                    double el = (DateTime.Now - animStart).TotalSeconds;
                    bool all = true;
                    for (int i = 0; i < prog.Length; i++)
                    {
                        float t = (float)((el - i * 0.035) / 0.42);
                        prog[i] = Skin.EaseOut(t);
                        if (prog[i] < 1f) all = false;
                    }
                    Invalidate();
                    if (all) anim.Stop();
                };
            }
            anim.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && anim != null) { anim.Stop(); anim.Dispose(); anim = null; }
            base.Dispose(disposing);
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
                float pr = (prog != null && i < prog.Length) ? prog[i] : 1f;
                if (pr <= 0.001f) continue;
                int y = i * RowH - scroll + (int)((1f - pr) * 10);   // подъезжает снизу

                if (i == hoverIdx)
                    using (SolidBrush hb = new SolidBrush(Color.FromArgb(28, 33, 47)))
                    using (GraphicsPath hp = Skin.Round(new RectangleF(0, y + 1, Width - 6, RowH - 2), 8))
                        g.FillPath(hb, hp);

                if (r.IsToday)
                    using (SolidBrush ab = new SolidBrush(Skin.A2))
                    using (GraphicsPath ap = Skin.Round(new RectangleF(0, y + 8, 3, RowH - 16), 1.5f))
                        g.FillPath(ab, ap);

                // GDI-текст не умеет альфу, поэтому «проявляем» цветом от фона
                Color dc = Skin.Mix(BackColor, r.Weekend ? Skin.Rose : Skin.Text, pr);
                TextRenderer.DrawText(g, r.Day.ToString("dd.MM"),
                    Skin.F(9.5f, r.IsToday ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(12, y, 56, RowH), dc,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, ru.TextInfo.ToTitleCase(r.Day.ToString("ddd", ru)),
                    Skin.F(8.5f, FontStyle.Regular),
                    new Rectangle(66, y, 48, RowH),
                    Skin.Mix(BackColor, r.Weekend ? Skin.Rose : Skin.Dim, pr),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                RectangleF track = new RectangleF(barX, y + RowH / 2f - 3.5f, barW, 7);
                using (GraphicsPath tp = Skin.Round(track, 3.5f))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(30, 36, 51)))
                    g.FillPath(tb, tp);

                double frac = BarScale > 0 ? r.Minutes / BarScale : 0;
                if (frac > 1) frac = 1;
                float w = (float)(barW * frac) * pr;   // полоска вырастает
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
                    Skin.Mix(BackColor, r.IsToday ? Skin.A2 : Skin.Text, pr),
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
        string lastRowsKey = "";
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        float sheen = 0;              // бегущий блик по карточке
        float popT = 0;               // «подскок» цифр при смене минуты
        string lastBig = "";
        System.Windows.Forms.Timer fade;
        float fadeT = 0, fadeTarget = 0;
        bool fadeHide = false;

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

        // ---------- настройки ----------
        internal double WidgetAlpha
        {
            get
            {
                string v; double d;
                if (settings.TryGetValue("alpha", out v) &&
                    double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return Math.Max(0.35, Math.Min(1.0, d));
                return 0.92;
            }
            set
            {
                double d = Math.Max(0.35, Math.Min(1.0, value));
                settings["alpha"] = d.ToString("0.###", CultureInfo.InvariantCulture);
                Store.SaveSettings(settings);
            }
        }

        internal bool HotkeyOn
        {
            get { string v; return settings.TryGetValue("hotkey", out v) && v == "1"; }
        }

        internal void SetHotkey(bool on)
        {
            settings["hotkey"] = on ? "1" : "0";
            Store.SaveSettings(settings);
            ApplyHotkey();
        }

        internal bool WidgetOn { get { return hud != null && !hud.IsDisposed; } }
        internal void RefreshWidgetAlpha() { if (WidgetOn) hud.ApplyAlpha(); }
        internal void ToggleWidgetPub() { ToggleWidget(); }
        internal bool AutostartOn { get { return IsAutostart(); } }
        internal void ToggleAutostartPub() { ToggleAutostart(); }

        // ---------- глобальная горячая клавиша ----------
        const int HOTKEY_ID = 0xA71;
        bool hotkeyRegistered;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        void ApplyHotkey()
        {
            if (!IsHandleCreated) return;
            if (hotkeyRegistered) { UnregisterHotKey(Handle, HOTKEY_ID); hotkeyRegistered = false; }
            if (!HotkeyOn) return;
            // MOD_ALT(1) | MOD_CONTROL(2) | MOD_NOREPEAT(0x4000), VK_SPACE = 0x20
            hotkeyRegistered = RegisterHotKey(Handle, HOTKEY_ID, 1 | 2 | 0x4000, 0x20);
            if (!hotkeyRegistered)
                MessageBox.Show("Не удалось занять Ctrl+Alt+Space — сочетание уже занято другой программой.",
                    "WorkTimer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // дескриптор появляется позже конструктора — регистрируем клавишу здесь
            ApplyHotkey();
        }

        protected override void WndProc(ref Message m)
        {
            // 0x0312 WM_HOTKEY, 0x0011 WM_QUERYENDSESSION, 0x0016 WM_ENDSESSION
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID) { ToggleRun(); return; }
            if (m.Msg == 0x0011 || m.Msg == 0x0016) FlushForShutdown();
            base.WndProc(ref m);
        }

        // закрыть текущий отрезок и записать всё на диск — вызывается при
        // завершении сеанса, спящем режиме и выходе, чтобы ничего не терялось
        void FlushForShutdown()
        {
            try
            {
                if (Running) CloseSegment();
                SaveNow();
            }
            catch { }
        }

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

            ApplyHotkey();

            // сохранить всё при завершении сеанса, засыпании и блокировке
            SystemEvents.SessionEnding += delegate { FlushForShutdown(); };
            SystemEvents.SessionSwitch += delegate { SaveNow(); };
            SystemEvents.PowerModeChanged += delegate(object s, PowerModeChangedEventArgs e)
            {
                if (e.Mode == PowerModes.Suspend) FlushForShutdown();
            };
            Application.ApplicationExit += delegate { FlushForShutdown(); };

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
            menu.Items.Add(new ToolStripMenuItem("Настройки", null, delegate { OpenSettings(); }));
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
            // ShowInTaskbar НЕ переключаем на лету: WinForms при смене этого
            // свойства пересоздаёт окно, а свёрнутое безрамочное окно без кнопки
            // на панели задач превращается в огрызок в углу экрана
            ShowInTaskbar = true;
            Icon = icoIdle;
            BackColor = Skin.Bg;
            ForeColor = Skin.Text;
            Font = Skin.F(9.5f, FontStyle.Regular);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            IntPtr rgn = CreateRoundRectRgn(0, 0, W + 1, H + 1, 20, 20);
            Region = Region.FromHrgn(rgn);
            DeleteObject(rgn);

            GButton btCfg = new GButton();
            btCfg.Style = GButton.Kind.Icon;
            btCfg.Text = "⚙";
            btCfg.Font = Skin.F(11f, FontStyle.Regular);
            btCfg.ForeColor = Skin.Muted;
            btCfg.Radius = 8;
            btCfg.SetBounds(W - 112, 14, 28, 28);
            btCfg.Click += delegate { OpenSettings(); };
            Controls.Add(btCfg);

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

            // Во время учёта карточка перерисовывается 20 раз в секунду.
            // Рисуем только то, что попало в область обновления.
            Rectangle clip = Rectangle.Ceiling(g.VisibleClipBounds);
            bool needHead = clip.IntersectsWith(new Rectangle(0, 0, W, 58));
            bool needHero = clip.IntersectsWith(HeroRect);
            bool needRest = clip.Bottom > MonthRect.Y - 40;

            // шапка
            if (needHead)
            using (GraphicsPath lp = Skin.Round(new RectangleF(20, 21, 14, 14), 4))
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new Rectangle(20, 21, 14, 14), Skin.A1, Skin.A2, LinearGradientMode.ForwardDiagonal))
                g.FillPath(lg, lp);
            if (needHead)
                TextRenderer.DrawText(g, "W O R K T I M E R", Skin.F(8.5f, FontStyle.Bold),
                    new Rectangle(44, 20, 240, 18), Skin.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // карточка «сегодня»
            if (needHero) {
            Color acc = running ? Skin.Green : (paused ? Skin.Amber : Skin.Dim);
            using (GraphicsPath hp = Skin.Round(HeroRect, 20))
            {
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    HeroRect, Color.FromArgb(26, 31, 45), Color.FromArgb(19, 23, 33),
                    LinearGradientMode.Vertical))
                    g.FillPath(lg, hp);

                if (running)
                {
                    // бегущий световой блик
                    float bw = HeroRect.Width * 0.30f;
                    float sx = HeroRect.X - bw + (sheen - (float)Math.Floor(sheen)) * (HeroRect.Width + bw * 2);
                    Region old = g.Clip;
                    g.SetClip(hp);
                    using (LinearGradientBrush lb = new LinearGradientBrush(
                        new RectangleF(sx, HeroRect.Y, bw, HeroRect.Height),
                        Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
                    {
                        ColorBlend cb = new ColorBlend(3);
                        cb.Colors = new Color[] {
                            Color.FromArgb(0, 255, 255, 255),
                            Color.FromArgb(14, 255, 255, 255),
                            Color.FromArgb(0, 255, 255, 255) };
                        cb.Positions = new float[] { 0f, 0.5f, 1f };
                        lb.InterpolationColors = cb;
                        g.FillRectangle(lb, sx, HeroRect.Y, bw, HeroRect.Height);
                    }
                    g.Clip = old;

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
            {
                Font bf = Skin.F(42f, FontStyle.Bold);
                Size bs = TextRenderer.MeasureText(g, big, bf, Size.Empty, TextFormatFlags.NoPadding);
                Rectangle br = new Rectangle(HeroRect.X + 20, HeroRect.Y + 50, bs.Width + 10, 66);

                // короткий «подскок» в момент смены минуты
                GraphicsState gs = null;
                if (popT > 0.001f)
                {
                    float sc = 1f + 0.05f * Skin.EaseOut(popT);
                    float px = br.X, py = br.Y + 33;
                    gs = g.Save();
                    g.TranslateTransform(px, py);
                    g.ScaleTransform(sc, sc);
                    g.TranslateTransform(-px, -py);
                }
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    br, Skin.Text, running ? Skin.A2 : Skin.Muted, LinearGradientMode.Horizontal))
                    g.DrawString(big, bf, lg, br.X, br.Y, StringFormat.GenericTypographic);
                if (gs != null) g.Restore(gs);
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
            }

            if (!needRest) return;

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
            {
                Font tf = Skin.F(17f, FontStyle.Bold);
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
            if (Visible && WindowState != FormWindowState.Minimized && !fadeHide) HideWindow();
            else
            {
                fadeHide = false;
                fadeT = 0;
                Opacity = 0;
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;
                Show();
                WindowState = FormWindowState.Normal;
                timer.Interval = 50;
                UpdateAll();
                Activate();
                BringToFront();
                FadeTo(1f, false);
            }
        }

        void HideWindow()
        {
            if (!Visible) return;
            FadeTo(0f, true);
        }

        void FadeTo(float target, bool hideAfter)
        {
            fadeTarget = target;
            fadeHide = hideAfter;
            if (fade == null)
            {
                fade = new System.Windows.Forms.Timer();
                fade.Interval = 15;
                fade.Tick += delegate
                {
                    bool done = true;
                    fadeT = Skin.Approach(fadeT, fadeTarget, 0.28f, ref done);
                    try { Opacity = fadeT; } catch { }
                    if (done)
                    {
                        fade.Stop();
                        if (fadeHide)
                        {
                            fadeHide = false;
                            Hide();
                            timer.Interval = 1000;
                            Opacity = 1;      // чтобы следующий показ начинался чисто
                            UpdateTrayText();
                            TrimMemory();
                        }
                    }
                };
            }
            fade.Start();
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
            settings["wy"] = hud.AnchorTop.ToString(CultureInfo.InvariantCulture);
            Store.SaveSettings(settings);
        }

        void OpenSettings()
        {
            bool wasHidden = !Visible;
            if (wasHidden) ToggleWindow();
            using (SettingsDialog d = new SettingsDialog(this))
                d.ShowDialog(this);
            SyncWidgetUi();
            miAutostart.Checked = IsAutostart();
            UpdateAll();
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
            DropCache(); lastRowsKey = "";
            SaveNow();
            UpdateAll();
        }

        void StopSession()
        {
            if (Running) CloseSegment();
            sessionAccumMin = 0;
            DropCache(); lastRowsKey = "";
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
            if (Running && (DateTime.Now - lastSave).TotalSeconds >= 15) SaveNow();

            // когда окно спрятано и счёт стоит, показывать нечего — просыпаемся
            // раз в 5 секунд вместо каждой секунды
            if (!Visible)
            {
                int want = Running ? 1000 : 5000;
                if (timer.Interval != want) timer.Interval = want;
            }

            if (Visible)
            {
                bool heroLive = false;
                if (Running) { pulse += 0.12f; sheen += 0.014f; heroLive = true; }
                if (popT > 0) { popT -= 0.055f; if (popT < 0) popT = 0; heroLive = true; }
                if (heroLive) Invalidate(HeroRect);
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

        // Пересчёт по всем отрезкам стоит недёшево, а отрисовка идёт до 20 раз
        // в секунду. Значение меняется не чаще раза в секунду — кэшируем.
        double todayCache = -1;
        DateTime todayCacheAt = DateTime.MinValue;

        internal void DropCache() { todayCache = -1; }

        double TodayMinutes()
        {
            DateTime now = DateTime.Now;
            if (todayCache >= 0 && (now - todayCacheAt).TotalMilliseconds < 900 &&
                now.Date == todayCacheAt.Date)
                return todayCache;

            Dictionary<DateTime, double> map = BuildDayMap();
            DateTime k = now.Date;
            todayCache = map.ContainsKey(k) ? map[k] : 0;
            todayCacheAt = now;
            return todayCache;
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
            if (!Visible && timer != null)
            {
                int want = Running ? 1000 : 5000;
                if (timer.Interval != want) timer.Interval = want;
            }
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

            string big = Fmt(TodayMinutes());
            if (lastBig.Length > 0 && big != lastBig) popT = 1f;
            lastBig = big;

            // список по дням пересобираем только когда есть что менять,
            // а не каждую секунду
            string rowsKey = viewMonth.ToString("yyyyMM") + "|" + segments.Count + "|" +
                             overrides.Count + "|" + big + "|" + running;
            if (rowsKey == lastRowsKey) { Invalidate(HeroRect); return; }
            lastRowsKey = rowsKey;

            CultureInfo ru = Ru;
            monthTitle = ru.TextInfo.ToTitleCase(viewMonth.ToString("MMMM yyyy", ru));

            Dictionary<DateTime, double> map = BuildDayMap();
            int days = DateTime.DaysInMonth(viewMonth.Year, viewMonth.Month);
            double total = 0, max = 480; int worked = 0;
            List<DayRow> rows = new List<DayRow>();

            for (int d = 1; d <= days; d++)
            {
                DateTime day = new DateTime(viewMonth.Year, viewMonth.Month, d);
                double m = map.ContainsKey(day) ? map[day] : 0;
                bool today = day.Date == DateTime.Now.Date;
                // сегодняшний день показываем всегда — иначе в пустой день
                // нельзя попасть двойным кликом и вписать часы вручную
                if (m <= 0 && !today) continue;
                if (m > 0) { total += m; worked++; }
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
            DropCache(); lastRowsKey = "";
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
            DateTime a = new DateTime(viewMonth.Year, viewMonth.Month, 1);
            DateTime b = a.AddMonths(1).AddDays(-1);

            DateTime from, to;
            using (RangeDialog rd = new RangeDialog(a, b))
            {
                if (rd.ShowDialog(this) != DialogResult.OK) return;
                from = rd.From.Date;
                to = rd.To.Date;
            }

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "CSV|*.csv";
            dlg.FileName = from.Year == to.Year && from.Month == to.Month
                ? string.Format("worktime_{0:yyyy-MM}.csv", from)
                : string.Format("worktime_{0:yyyy-MM-dd}_{1:yyyy-MM-dd}.csv", from, to);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            Dictionary<DateTime, double> map = BuildDayMap();
            CultureInfo ru = Ru;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Дата;День недели;Часы (чч:мм);Часы (десятичные)");
            double total = 0;
            int worked = 0;
            for (DateTime day = from; day <= to; day = day.AddDays(1))
            {
                double m = map.ContainsKey(day) ? map[day] : 0;
                if (m <= 0) continue;
                total += m; worked++;
                sb.AppendLine(string.Format("{0};{1};{2};{3}",
                    day.ToString("dd.MM.yyyy"), day.ToString("ddd", ru), Fmt(m),
                    (m / 60.0).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')));
            }
            sb.AppendLine();
            sb.AppendLine(string.Format("Период;{0} — {1};;", from.ToString("dd.MM.yyyy"),
                to.ToString("dd.MM.yyyy")));
            sb.AppendLine(string.Format("Рабочих дней;{0};;", worked));
            sb.AppendLine(string.Format("ИТОГО;;{0};{1}", Fmt(total),
                (total / 60.0).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')));
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(string.Format("Сохранено: {0}{3}{1} дн., всего {2}",
                dlg.FileName, worked, Fmt(total), Environment.NewLine), "WorkTimer");
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
            if (hotkeyRegistered) { UnregisterHotKey(Handle, HOTKEY_ID); hotkeyRegistered = false; }
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
        const int WW = 214, WHBase = 70, PanelH = 46;

        bool hover, overBtn, pressBtn, overChev, dragging, armed, draggingSlider;
        bool expanded;
        float panelT;                    // 0 — свёрнут, 1 — панель раскрыта
        float pulse = 0;
        float fadeT = 0;                 // плавное появление
        double userAlpha = 0.92;         // прозрачность, заданная пользователем
        Point grabScreen, grabOrigin;
        string lastKey = "";

        Bitmap buf;                      // переиспользуемый холст
        Graphics bufG;
        System.Windows.Forms.Timer tick, fadeIn, panelAnim;

        public HudWidget(TrayApp owner)
        {
            app = owner;
            userAlpha = app.WidgetAlpha;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(WW, WHBase);
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
                cp.ExStyle |= 0x00000080;  // WS_EX_TOOLWINDOW — мимо Alt+Tab
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            fadeT = 0;
            Render();
            if (fadeIn == null)
            {
                fadeIn = new System.Windows.Forms.Timer();
                fadeIn.Interval = 15;
                fadeIn.Tick += delegate
                {
                    bool done = true;
                    fadeT = Skin.Approach(fadeT, 1f, 0.26f, ref done);
                    Render();
                    if (done) fadeIn.Stop();
                };
            }
            fadeIn.Start();
        }

        // Позицию запоминаем так, будто панель свёрнута: иначе после
        // перезапуска виджет уезжал бы вверх на высоту панели
        public int AnchorTop { get { return Top + (Height - WHBase); } }

        public void ApplyAlpha()
        {
            userAlpha = app.WidgetAlpha;
            Render();
        }

        void Beat()
        {
            bool run = app.RunningPub;
            // пока идёт учёт — 8 кадров в секунду ради пульсации точки,
            // иначе достаточно раза в секунду
            tick.Interval = run ? 120 : 1000;
            if (run) pulse += 0.14f;
            string key = Key();
            if (run || key != lastKey) { lastKey = key; Render(); }
        }

        string Key()
        {
            return app.RunningPub + "|" + TrayApp.Fmt(app.TodayPub) + "|" +
                   TrayApp.Fmt(app.SessionPub) + "|" + hover + overBtn + overChev + expanded;
        }

        public void Refresh2() { lastKey = ""; Beat(); }

        // ---------- геометрия ----------
        int Oy { get { return Height - WHBase; } }                       // сдвиг основной строки
        Rectangle BtnRect { get { return new Rectangle(WW - 52, Oy + WHBase / 2 - 17, 34, 34); } }
        Rectangle ChevRect { get { return new Rectangle(WW - 30, Oy + 3, 24, 16); } }
        RectangleF Track { get { return new RectangleF(18, 30, WW - 36, 5); } }

        float AlphaToT(double a) { return (float)((a - 0.35) / 0.65); }
        double TToAlpha(float t) { return 0.35 + Math.Max(0f, Math.Min(1f, t)) * 0.65; }

        // ---------- раскрытие панели ----------
        void ToggleExpand()
        {
            expanded = !expanded;
            int bottom = Top + Height;
            if (panelAnim == null)
            {
                panelAnim = new System.Windows.Forms.Timer();
                panelAnim.Interval = 16;
                panelAnim.Tick += delegate
                {
                    bool done = true;
                    panelT = Skin.Approach(panelT, expanded ? 1f : 0f, 0.26f, ref done);
                    int h = WHBase + (int)Math.Round(PanelH * panelT);
                    SetBounds(Left, panelBottom - h, WW, h);
                    Render();
                    if (done) { panelAnim.Stop(); app.SaveWidgetPos(); }
                };
            }
            panelBottom = bottom;
            panelAnim.Start();
        }
        int panelBottom;

        // ---------- отрисовка ----------
        void Render()
        {
            if (!IsHandleCreated) return;
            EnsureSurface();
            if (bufG == null) return;

            Graphics g = bufG;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            bool run = app.RunningPub;
            bool paused = !run && app.SessionPub > 0;
            Color acc = run ? Skin.Green : (paused ? Skin.Amber : Skin.Muted);
            int bgA = hover ? 246 : 216;
            int oy = Oy;

            RectangleF card = new RectangleF(1, 1, WW - 2, Height - 2);
            using (GraphicsPath p = Skin.Round(card, 18))
            {
                using (LinearGradientBrush lg = new LinearGradientBrush(card,
                    Color.FromArgb(bgA, 24, 28, 40), Color.FromArgb(bgA, 14, 17, 25),
                    LinearGradientMode.Vertical))
                    g.FillPath(lg, p);
                using (Pen pn = new Pen(Color.FromArgb(run ? 90 : 46, acc), 1.4f))
                    g.DrawPath(pn, p);
            }

            // --- панель прозрачности ---
            if (panelT > 0.01f)
            {
                int a = (int)(255 * panelT);
                using (SolidBrush lb = new SolidBrush(Color.FromArgb((int)(180 * panelT), Skin.Dim)))
                    g.DrawString("ПРОЗРАЧНОСТЬ", Skin.F(7f, FontStyle.Bold), lb, 17, 9,
                        StringFormat.GenericTypographic);

                string pct = Math.Round(userAlpha * 100) + "%";
                using (SolidBrush vb = new SolidBrush(Color.FromArgb(a, Skin.Text)))
                {
                    SizeF sz = g.MeasureString(pct, Skin.F(7.5f, FontStyle.Bold),
                        1000, StringFormat.GenericTypographic);
                    g.DrawString(pct, Skin.F(7.5f, FontStyle.Bold), vb,
                        WW - 18 - sz.Width, 8, StringFormat.GenericTypographic);
                }

                RectangleF tr = Track;
                using (GraphicsPath tp = Skin.Round(tr, 2.5f))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb((int)(90 * panelT), 90, 105, 140)))
                    g.FillPath(tb, tp);

                float t = AlphaToT(userAlpha);
                RectangleF fr = new RectangleF(tr.X, tr.Y, tr.Width * t, tr.Height);
                if (fr.Width > 1)
                    using (GraphicsPath fp = Skin.Round(fr, 2.5f))
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new RectangleF(tr.X, tr.Y, tr.Width, tr.Height),
                        Color.FromArgb(a, Skin.A1), Color.FromArgb(a, Skin.A2),
                        LinearGradientMode.Horizontal))
                        g.FillPath(lg, fp);

                float kx = tr.X + tr.Width * t;
                float kr = draggingSlider ? 8f : 7f;
                using (SolidBrush ks = new SolidBrush(Color.FromArgb((int)(70 * panelT), 0, 0, 0)))
                    g.FillEllipse(ks, kx - kr, tr.Y + tr.Height / 2 - kr + 1, kr * 2, kr * 2);
                using (SolidBrush kb = new SolidBrush(Color.FromArgb(a, 245, 248, 255)))
                    g.FillEllipse(kb, kx - kr, tr.Y + tr.Height / 2 - kr, kr * 2, kr * 2);
            }

            // --- стрелка раскрытия ---
            {
                Rectangle cr = ChevRect;
                float cx = cr.X + cr.Width / 2f, cy = cr.Y + cr.Height / 2f;
                int ca = overChev ? 235 : 130;
                using (Pen cp = new Pen(Color.FromArgb(ca, Skin.Text), 1.6f))
                {
                    cp.StartCap = LineCap.Round; cp.EndCap = LineCap.Round;
                    float dir = panelT > 0.5f ? -1f : 1f;   // раскрыто — стрелка вниз
                    g.DrawLine(cp, cx - 4, cy + 2 * dir, cx, cy - 2 * dir);
                    g.DrawLine(cp, cx, cy - 2 * dir, cx + 4, cy + 2 * dir);
                }
            }

            // --- точка состояния ---
            float k2 = run ? (float)(0.5 + 0.5 * Math.Sin(pulse)) : 1f;
            float dr = run ? 5f + 2f * k2 : 5f;
            if (run)
                using (SolidBrush halo = new SolidBrush(Color.FromArgb((int)(70 * k2), acc)))
                    g.FillEllipse(halo, 17 - dr - 4, oy + WHBase / 2f - dr - 4, (dr + 4) * 2, (dr + 4) * 2);
            using (SolidBrush db = new SolidBrush(acc))
                g.FillEllipse(db, 17 - 4.5f, oy + WHBase / 2f - 4.5f, 9, 9);

            // --- время ---
            string big = TrayApp.Fmt(app.TodayPub);
            Font bf = Skin.F(19f, FontStyle.Bold);
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new RectangleF(32, oy + 10, 110, 30), Color.FromArgb(250, 245, 248, 255),
                run ? Color.FromArgb(250, Skin.A2) : Color.FromArgb(230, Skin.Muted),
                LinearGradientMode.Horizontal))
                g.DrawString(big, bf, lg, 31, oy + 11, StringFormat.GenericTypographic);

            string sub = run || paused ? "сессия " + TrayApp.Fmt(app.SessionPub) : "остановлен";
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(190, Skin.Dim)))
                g.DrawString(sub, Skin.F(7.5f, FontStyle.Regular), sb, 33, oy + 43,
                    StringFormat.GenericTypographic);

            // --- кнопка старт/пауза ---
            Rectangle br = BtnRect;
            Color bc = overBtn
                ? (pressBtn ? Color.FromArgb(255, Skin.Card3) : Color.FromArgb(240, Skin.Card3))
                : Color.FromArgb(150, Skin.Card2);
            float shrink = pressBtn ? 2f : 0f;
            RectangleF br2 = new RectangleF(br.X + shrink, br.Y + shrink,
                br.Width - shrink * 2, br.Height - shrink * 2);
            using (SolidBrush bb = new SolidBrush(bc)) g.FillEllipse(bb, br2);
            if (overBtn)
                using (Pen pn = new Pen(Color.FromArgb(120, acc), 1.2f)) g.DrawEllipse(pn, br2);

            float bx = br.X + br.Width / 2f, by = br.Y + br.Height / 2f;
            using (SolidBrush w = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                if (run)
                {
                    g.FillRectangle(w, bx - 4.5f, by - 6, 3.2f, 12);
                    g.FillRectangle(w, bx + 1.3f, by - 6, 3.2f, 12);
                }
                else
                    g.FillPolygon(w, new PointF[] {
                        new PointF(bx - 4, by - 6.5f), new PointF(bx + 6, by),
                        new PointF(bx - 4, by + 6.5f) });
            }

            // --- прогресс до 8 часов ---
            float frac = (float)Math.Min(1.0, app.TodayPub / 480.0);
            RectangleF tr2 = new RectangleF(16, oy + WHBase - 11, WW - 32, 3);
            using (GraphicsPath tp = Skin.Round(tr2, 1.5f))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(70, 90, 105, 140)))
                g.FillPath(tb, tp);
            if (frac > 0.005f)
            {
                RectangleF fr2 = new RectangleF(tr2.X, tr2.Y, tr2.Width * frac, 3);
                using (GraphicsPath fp = Skin.Round(fr2, 1.5f))
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    new RectangleF(tr2.X, tr2.Y, tr2.Width, 3),
                    frac >= 1f ? Skin.Green : Skin.A1, Skin.A2, LinearGradientMode.Horizontal))
                    g.FillPath(lg, fp);
            }

            Flush();
        }

        // ---------- слоёное окно ----------
        // Поверхность создаётся один раз (DIB section) и переиспользуется.
        // Раньше на каждый кадр вызывался GetHbitmap — это копия картинки
        // и новый объект GDI восемь раз в секунду.
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi,
            uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst,
            ref PT ppt, ref SZ psz, IntPtr src, ref PT pptSrc, int key, ref BLEND bl, int flags);

        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential)]
        struct BLEND { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO { public BITMAPINFOHEADER h; public uint c0, c1, c2; }

        IntPtr memDc, dib, oldBmp;

        void EnsureSurface()
        {
            if (dib != IntPtr.Zero && buf != null && buf.Width == Width && buf.Height == Height)
                return;
            ReleaseSurface();

            BITMAPINFO bi = new BITMAPINFO();
            bi.h.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.h.biWidth = Width;
            bi.h.biHeight = -Height;        // сверху вниз
            bi.h.biPlanes = 1;
            bi.h.biBitCount = 32;
            bi.h.biCompression = 0;         // BI_RGB

            IntPtr screen = GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(screen);
            IntPtr bits;
            dib = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
            ReleaseDC(IntPtr.Zero, screen);
            if (dib == IntPtr.Zero) return;

            oldBmp = SelectObject(memDc, dib);
            buf = new Bitmap(Width, Height, Width * 4,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb, bits);
            bufG = Graphics.FromImage(buf);
        }

        void ReleaseSurface()
        {
            if (bufG != null) { bufG.Dispose(); bufG = null; }
            if (buf != null) { buf.Dispose(); buf = null; }
            if (memDc != IntPtr.Zero)
            {
                if (oldBmp != IntPtr.Zero) { SelectObject(memDc, oldBmp); oldBmp = IntPtr.Zero; }
                DeleteDC(memDc); memDc = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        }

        void Flush()
        {
            SZ sz; sz.W = Width; sz.H = Height;
            PT src; src.X = 0; src.Y = 0;
            PT pos; pos.X = Left; pos.Y = Top;
            BLEND bl;
            bl.Op = 0; bl.Flags = 0; bl.Format = 1;   // AC_SRC_ALPHA
            double a = fadeT * userAlpha * 255;
            bl.Alpha = (byte)Math.Max(0, Math.Min(255, (int)a));
            // hdcDst = NULL — система сама возьмёт экранный контекст
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref sz, memDc, ref src, 0, ref bl, 2);
        }

        // ---------- мышь ----------
        bool InSlider(Point p)
        {
            if (panelT < 0.6f) return false;
            RectangleF tr = Track;
            return p.Y >= tr.Y - 12 && p.Y <= tr.Y + tr.Height + 12 &&
                   p.X >= tr.X - 10 && p.X <= tr.Right + 10;
        }

        void SetAlphaFromX(int x)
        {
            RectangleF tr = Track;
            float t = (x - tr.X) / tr.Width;
            userAlpha = TToAlpha(t);
            Render();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true; Render(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false; overBtn = false; overChev = false; pressBtn = false;
            Render(); base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingSlider) { SetAlphaFromX(e.X); return; }

            if (armed)
            {
                Point now = PointToScreen(e.Location);
                int dx = now.X - grabScreen.X, dy = now.Y - grabScreen.Y;
                if (!dragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)) dragging = true;
                if (dragging) Location = new Point(grabOrigin.X + dx, grabOrigin.Y + dy);
                return;
            }

            bool ob = BtnRect.Contains(e.Location);
            bool oc = ChevRect.Contains(e.Location);
            Cursor = (ob || oc || InSlider(e.Location)) ? Cursors.Hand : Cursors.SizeAll;
            if (ob != overBtn || oc != overChev) { overBtn = ob; overChev = oc; Render(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { app.ShowMenuAt(PointToScreen(e.Location)); return; }
            if (e.Button != MouseButtons.Left) return;

            if (ChevRect.Contains(e.Location)) { ToggleExpand(); return; }
            if (InSlider(e.Location)) { draggingSlider = true; SetAlphaFromX(e.X); return; }
            if (BtnRect.Contains(e.Location)) { pressBtn = true; Render(); return; }

            armed = true;
            dragging = false;
            grabScreen = PointToScreen(e.Location);
            grabOrigin = Location;
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (draggingSlider)
            {
                draggingSlider = false;
                app.WidgetAlpha = userAlpha;
                Render();
                return;
            }
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
            if (!BtnRect.Contains(e.Location) && !ChevRect.Contains(e.Location) &&
                !InSlider(e.Location))
                app.OpenMainPub();
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
                if (fadeIn != null) { fadeIn.Stop(); fadeIn.Dispose(); fadeIn = null; }
                if (panelAnim != null) { panelAnim.Stop(); panelAnim.Dispose(); panelAnim = null; }
                ReleaseSurface();
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

    // ================= ПОЛЗУНОК =================
    class GSlider : Control
    {
        public float Value = 1f;                 // 0..1
        public Action<float> Changed;
        bool drag, hover;

        public GSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Height = 28;
        }

        RectangleF Track { get { return new RectangleF(9, Height / 2f - 2.5f, Width - 18, 5); } }

        void SetFromX(int x)
        {
            RectangleF t = Track;
            float v = (x - t.X) / t.Width;
            Value = Math.Max(0f, Math.Min(1f, v));
            if (Changed != null) Changed(Value);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            drag = false;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);

            RectangleF t = Track;
            using (GraphicsPath tp = Skin.Round(t, 2.5f))
            using (SolidBrush tb = new SolidBrush(Skin.Card3))
                g.FillPath(tb, tp);

            RectangleF f = new RectangleF(t.X, t.Y, t.Width * Value, t.Height);
            if (f.Width > 1)
                using (GraphicsPath fp = Skin.Round(f, 2.5f))
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    new RectangleF(t.X, t.Y, t.Width, t.Height), Skin.A1, Skin.A2,
                    LinearGradientMode.Horizontal))
                    g.FillPath(lg, fp);

            float kx = t.X + t.Width * Value;
            float kr = drag ? 9f : (hover ? 8.5f : 7.5f);
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                g.FillEllipse(sh, kx - kr, t.Y + t.Height / 2 - kr + 1.5f, kr * 2, kr * 2);
            using (SolidBrush kb = new SolidBrush(Color.FromArgb(245, 248, 255)))
                g.FillEllipse(kb, kx - kr, t.Y + t.Height / 2 - kr, kr * 2, kr * 2);
        }
    }

    // ================= НАСТРОЙКИ =================
    class SettingsDialog : Form
    {
        readonly TrayApp app;
        GButton bWidget, bHotkey, bAuto, bClose, bDone;
        GSlider slider;

        const int W = 420, H = 356;

        public SettingsDialog(TrayApp owner)
        {
            app = owner;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, H);
            BackColor = Skin.Card;
            ShowInTaskbar = false;
            Font = Skin.F(9.5f, FontStyle.Regular);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            bClose = Mk(GButton.Kind.Icon, "✕", W - 44, 14, 28, 28);
            bClose.Danger = true; bClose.Radius = 8;
            bClose.Click += delegate { Close(); };

            bWidget = Mk(GButton.Kind.Ghost, "", W - 140, 72, 116, 34);
            bWidget.Click += delegate { app.ToggleWidgetPub(); Sync(); };

            slider = new GSlider();
            slider.BackColor = Skin.Card;
            slider.SetBounds(20, 150, W - 40, 28);
            slider.Changed += delegate(float v)
            {
                app.WidgetAlpha = 0.35 + v * 0.65;
                app.RefreshWidgetAlpha();
                Invalidate(new Rectangle(0, 118, W, 30));
            };
            Controls.Add(slider);

            bHotkey = Mk(GButton.Kind.Ghost, "", W - 140, 198, 116, 34);
            bHotkey.Click += delegate { app.SetHotkey(!app.HotkeyOn); Sync(); };

            bAuto = Mk(GButton.Kind.Ghost, "", W - 140, 244, 116, 34);
            bAuto.Click += delegate { app.ToggleAutostartPub(); Sync(); };

            bDone = Mk(GButton.Kind.Primary, "Готово", 20, H - 52, 120, 36);
            bDone.Font = Skin.F(9.5f, FontStyle.Bold);
            bDone.Click += delegate { Close(); };

            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < 56)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 2, 0);
                }
            };

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
            Sync();
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);

        GButton Mk(GButton.Kind k, string text, int x, int y, int w, int h)
        {
            GButton b = new GButton();
            b.Style = k;
            b.Text = text;
            b.ParentBg = Skin.Card;
            b.ForeColor = Skin.Muted;
            b.Radius = 11;
            b.SetBounds(x, y, w, h);
            Controls.Add(b);
            return b;
        }

        void Mark(GButton b, bool on)
        {
            b.Text = on ? "включено" : "выключено";
            b.ForeColor = on ? Skin.A2 : Skin.Dim;
            b.Invalidate();
        }

        void Sync()
        {
            Mark(bWidget, app.WidgetOn);
            Mark(bHotkey, app.HotkeyOn);
            Mark(bAuto, app.AutostartOn);
            slider.Value = (float)((app.WidgetAlpha - 0.35) / 0.65);
            slider.Invalidate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Skin.Card)) g.FillRectangle(bg, ClientRectangle);
            using (Pen p = new Pen(Skin.Card3, 1f))
                g.DrawRectangle(p, 0, 0, W - 1, H - 1);
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new Rectangle(0, 0, W, 3), Skin.A1, Skin.A2, LinearGradientMode.Horizontal))
                g.FillRectangle(lg, 0, 0, W, 3);

            TextRenderer.DrawText(g, "Настройки", Skin.F(13f, FontStyle.Bold),
                new Rectangle(22, 18, 300, 28), Skin.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);

            Row(g, 72, "Виджет на экране", "маленькая плашка поверх окон");
            TextRenderer.DrawText(g, "Прозрачность виджета", Skin.F(9.5f, FontStyle.Regular),
                new Rectangle(22, 122, 260, 20), Skin.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Math.Round(app.WidgetAlpha * 100) + "%",
                Skin.F(9.5f, FontStyle.Bold),
                new Rectangle(W - 90, 122, 68, 20), Skin.A2,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);

            Row(g, 198, "Ctrl + Alt + Space", "глобально: старт и пауза, не открывая окно");
            Row(g, 244, "Запускать с Windows", "стартовать вместе с системой");

            using (Pen p = new Pen(Color.FromArgb(32, 38, 54), 1f))
            {
                g.DrawLine(p, 20, 112, W - 20, 112);
                g.DrawLine(p, 20, 188, W - 20, 188);
                g.DrawLine(p, 20, H - 66, W - 20, H - 66);
            }
        }

        void Row(Graphics g, int y, string title, string hint)
        {
            TextRenderer.DrawText(g, title, Skin.F(9.5f, FontStyle.Regular),
                new Rectangle(22, y + 2, 260, 20), Skin.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, hint, Skin.F(8f, FontStyle.Regular),
                new Rectangle(22, y + 19, 260, 18), Skin.Dim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    // ================= ВЫБОР ПЕРИОДА =================
    class RangeDialog : Form
    {
        DateTime from, to;
        public DateTime From { get { return from; } }
        public DateTime To { get { return to; } }
        DateTime view;
        DateTime? pickFrom;
        bool picking;
        int hoverDay = -1;

        GButton bPrev, bNext, bClose, bGo, bCancel;
        GButton[] presets;

        const int W = 372, H = 476;
        const int CellW = 46, CellH = 34;
        static readonly Point Grid = new Point(20, 164);

        public RangeDialog(DateTime from, DateTime to)
        {
            this.from = from; this.to = to;
            view = new DateTime(to.Year, to.Month, 1);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(W, H);
            BackColor = Skin.Card;
            ShowInTaskbar = false;
            Font = Skin.F(9.5f, FontStyle.Regular);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            bClose = Mk(GButton.Kind.Icon, "✕", W - 44, 14, 28, 28, 8);
            bClose.Danger = true;
            bClose.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            bPrev = Mk(GButton.Kind.Icon, "‹", 20, 68, 30, 30, 9);
            bPrev.Font = Skin.F(14f, FontStyle.Bold);
            bPrev.Click += delegate { view = view.AddMonths(-1); Invalidate(); };

            bNext = Mk(GButton.Kind.Icon, "›", 54, 68, 30, 30, 9);
            bNext.Font = Skin.F(14f, FontStyle.Bold);
            bNext.Click += delegate { view = view.AddMonths(1); Invalidate(); };

            string[] names = { "Этот месяц", "Прошлый", "Этот год", "Всё время" };
            presets = new GButton[4];
            int px = 20;
            for (int i = 0; i < 4; i++)
            {
                int w = i == 0 ? 86 : (i == 1 ? 74 : (i == 2 ? 72 : 82));
                GButton b = Mk(GButton.Kind.Ghost, names[i], px, 106, w, 30, 10);
                b.Font = Skin.F(8.5f, FontStyle.Regular);
                int idx = i;
                b.Click += delegate { Preset(idx); };
                presets[i] = b;
                px += w + 5;
            }

            bGo = Mk(GButton.Kind.Primary, "Экспорт", W - 150, H - 54, 130, 38, 12);
            bGo.Font = Skin.F(9.5f, FontStyle.Bold);
            bGo.Click += delegate { DialogResult = DialogResult.OK; Close(); };

            bCancel = Mk(GButton.Kind.Ghost, "Отмена", 20, H - 54, 110, 38, 12);
            bCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseLeave += delegate { if (hoverDay != -1) { hoverDay = -1; Invalidate(); } };
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
                if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
            };
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);

        GButton Mk(GButton.Kind k, string text, int x, int y, int w, int h, float rad)
        {
            GButton b = new GButton();
            b.Style = k; b.Text = text; b.ParentBg = Skin.Card;
            b.ForeColor = Skin.Muted; b.Radius = rad;
            b.SetBounds(x, y, w, h);
            Controls.Add(b);
            return b;
        }

        void Preset(int i)
        {
            DateTime now = DateTime.Now.Date;
            if (i == 0) { from = new DateTime(now.Year, now.Month, 1); to = from.AddMonths(1).AddDays(-1); }
            else if (i == 1)
            {
                DateTime m = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                from = m; to = m.AddMonths(1).AddDays(-1);
            }
            else if (i == 2) { from = new DateTime(now.Year, 1, 1); to = new DateTime(now.Year, 12, 31); }
            else { from = new DateTime(2000, 1, 1); to = now; }
            view = new DateTime(to.Year, to.Month, 1);
            picking = false; pickFrom = null;
            Invalidate();
        }

        // первый день сетки — понедельник недели, в которой 1-е число
        DateTime GridStart()
        {
            DateTime first = new DateTime(view.Year, view.Month, 1);
            int dow = ((int)first.DayOfWeek + 6) % 7;      // Пн = 0
            return first.AddDays(-dow);
        }

        int CellAt(Point p)
        {
            int cx = (p.X - Grid.X) / CellW;
            int cy = (p.Y - Grid.Y) / CellH;
            if (cx < 0 || cx > 6 || cy < 0 || cy > 5) return -1;
            if (p.X < Grid.X || p.Y < Grid.Y) return -1;
            return cy * 7 + cx;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            int c = CellAt(e.Location);
            if (c != hoverDay) { hoverDay = c; Invalidate(); }
        }

        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < 56)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 2, 0);
                return;
            }
            int c = CellAt(e.Location);
            if (c < 0) return;
            DateTime d = GridStart().AddDays(c);

            if (!picking) { pickFrom = d; from = d; to = d; picking = true; }
            else
            {
                DateTime a = pickFrom.Value;
                from = d < a ? d : a;
                to = d < a ? a : d;
                picking = false;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Skin.Card)) g.FillRectangle(bg, ClientRectangle);
            using (Pen p = new Pen(Skin.Card3, 1f)) g.DrawRectangle(p, 0, 0, W - 1, H - 1);
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new Rectangle(0, 0, W, 3), Skin.A1, Skin.A2, LinearGradientMode.Horizontal))
                g.FillRectangle(lg, 0, 0, W, 3);

            CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");

            TextRenderer.DrawText(g, "Экспорт за период", Skin.F(13f, FontStyle.Bold),
                new Rectangle(22, 18, 300, 28), Skin.Text,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);

            TextRenderer.DrawText(g, ru.TextInfo.ToTitleCase(view.ToString("MMMM yyyy", ru)),
                Skin.F(11f, FontStyle.Bold), new Rectangle(96, 68, 200, 30), Skin.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            string[] wd = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
            for (int i = 0; i < 7; i++)
                TextRenderer.DrawText(g, wd[i], Skin.F(8f, FontStyle.Bold),
                    new Rectangle(Grid.X + i * CellW, Grid.Y - 20, CellW, 16),
                    i >= 5 ? Skin.Rose : Skin.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

            DateTime start = GridStart();
            for (int i = 0; i < 42; i++)
            {
                DateTime d = start.AddDays(i);
                int cx = Grid.X + (i % 7) * CellW;
                int cy = Grid.Y + (i / 7) * CellH;
                bool inMonth = d.Month == view.Month;
                bool inRange = d.Date >= from.Date && d.Date <= to.Date;
                bool edge = d.Date == from.Date || d.Date == to.Date;

                RectangleF cell = new RectangleF(cx + 2, cy + 2, CellW - 4, CellH - 4);
                if (inRange && !edge)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(38, Skin.A2)))
                    using (GraphicsPath p = Skin.Round(cell, 8))
                        g.FillPath(b, p);
                if (edge)
                    using (GraphicsPath p = Skin.Round(cell, 9))
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        cell, Skin.A1, Skin.A2, LinearGradientMode.Horizontal))
                        g.FillPath(lg, p);
                else if (i == hoverDay)
                    using (SolidBrush b = new SolidBrush(Skin.Card2))
                    using (GraphicsPath p = Skin.Round(cell, 8))
                        g.FillPath(b, p);

                if (d.Date == DateTime.Now.Date && !edge)
                    using (Pen p = new Pen(Skin.A2, 1.2f))
                    using (GraphicsPath gp = Skin.Round(cell, 8))
                        g.DrawPath(p, gp);

                Color tc = edge ? Color.White
                    : (!inMonth ? Color.FromArgb(60, 68, 88)
                    : (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday
                       ? Skin.Rose : Skin.Text));
                TextRenderer.DrawText(g, d.Day.ToString(),
                    Skin.F(9.5f, edge ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(cx, cy, CellW, CellH), tc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
            }

            int days = (int)(to.Date - from.Date).TotalDays + 1;
            string info = from.ToString("dd.MM.yyyy") + "  —  " + to.ToString("dd.MM.yyyy") +
                          "   (" + days + " дн.)";
            TextRenderer.DrawText(g, info, Skin.F(9.5f, FontStyle.Bold),
                new Rectangle(20, H - 96, W - 40, 22), Skin.A2,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,
                picking ? "выбери вторую дату" : "клик — начало, второй клик — конец",
                Skin.F(8f, FontStyle.Regular),
                new Rectangle(20, H - 76, W - 40, 18), Skin.Dim,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
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
