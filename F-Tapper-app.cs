using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

internal static class Program {
    private const int VK_XBUTTON1 = 0x05;
    private const int VK_XBUTTON2 = 0x06;
    private const int WH_MOUSE_LL = 14;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int XBUTTON1 = 1;
    private const int XBUTTON2 = 2;
    private const int RIDEV_INPUTSINK = 0x00000100;
    private const int RIDEV_EXINPUTSINK = 0x00001000;
    private const int RID_INPUT = 0x10000003;
    private const int RIM_TYPEMOUSE = 0;
    private const int WM_INPUT = 0x00FF;
    private const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    private const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const int DIGCF_PRESENT = 2;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private static readonly Guid HidGuid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
    private static readonly Guid UsbGuid = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    private const string BoardVidPid = "VID_16C0&PID_27E9";
    private const string BootVidPid = "VID_16D0&PID_0753";
    private const byte HidCmdSetKey = 0xA5;
    private const byte HidCmdToggle = 0xA6;
    private const byte HidCmdSetSpeed = 0xA7;
    private const byte HidCmdOff = 0xA8;
    private const byte HidCmdOn = 0xA9;
    private static readonly object _hidGate = new object();
    private static int _cmdMousePending;
    private static DateTime _cmdMouseAt = DateTime.MinValue;
    private static bool _needEnableSync;
    private static DateTime _linkAt = DateTime.MinValue;
    private static bool _resumeAfterCapture;

    private static readonly Color UiBg = Color.FromArgb(246, 247, 250);
    private static readonly Color UiText = Color.FromArgb(28, 28, 30);
    private static readonly Color UiMuted = Color.FromArgb(110, 116, 128);
    private static readonly Color UiLine = Color.FromArgb(220, 224, 232);
    private static readonly Color UiAccent = Color.FromArgb(37, 99, 235);
    private static readonly Color UiChip = Color.White;
    private static readonly Font FontUi = new Font("Segoe UI", 9f);
    private const string HelpText =
        "Платы\r\n" +
        "Подходит Digispark на ATtiny85 с загрузчиком micronucleus, такт 16.5 МГц.\r\n" +
        "Клоны этой платы тоже подходят.\r\n" +
        "Не подходит: Digispark Pro, обычные Arduino и платы без micronucleus.\r\n\r\n" +
        "Права администратора\r\n" +
        "Windows спрашивает их при запуске.\r\n" +
        "Без них боковая кнопка мыши не ловится в игре, запущенной от администратора.\r\n" +
        "Команда на плату и прошивка тоже обычно не проходят.\r\n\r\n" +
        "Плата сама тапает клавишу. Программа её включает и выключает.\r\n\r\n" +
        "Связь\r\n" +
        "Кружок сверху — плата на USB. В трее то же: зелёный TAP — на связи, красный — нет.\r\n\r\n" +
        "Тапер\r\n" +
        "Клик по «тапер» включает. Ещё раз — выключает.\r\n" +
        "Крестик закрывает программу. Свернуть — в трей. Двойной клик по значку — открыть.\r\n\r\n" +
        "Настройки\r\n" +
        "Клавиша — что тапает плата. Нажми поле и клавишу на клавиатуре.\r\n" +
        "Команда — чем включать: боковая кнопка мыши или сочетание, например Ctrl+A.\r\n" +
        "Обе боковые: в режиме захвата нажми Back, затем Forward за полсекунды (или наоборот).\r\n" +
        "После новой команды окно отпускает фокус, чтобы тап не попал сюда.\r\n" +
        "Скорость: 1 — чаще 209–322 мс, 10 — чаще 49–282 мс. Короткие и длинные паузы реже.\r\n" +
        "Маячок у ползунка моргает в том же ритме.\r\n" +
        "Если плата не приняла настройку — нажми «Прошить».\r\n\r\n" +
        "Прошивка\r\n" +
        "1. Нажми «Прошить». Тапер при этом выключается.\r\n" +
        "2. Если на плате уже эта прошивка — запись не начинается.\r\n" +
        "3. Если драйвера нет, программа ставит его сама. Потом снова «Прошить».\r\n" +
        "   Если не встал, файл Digistump.Drivers.zip:\r\n" +
        "   https://github.com/digistump/DigistumpArduino/releases/tag/1.6.7\r\n" +
        "   Распакуй и запусти DPinst64.exe. Потом снова «Прошить».\r\n" +
        "4. Выдерни USB. Когда появится «Воткни» — воткни сразу.\r\n" +
        "Пока драйвер установлен, включённый тапер может тормозить компьютер.\r\n" +
        "«Снять драйвер» убирает его.";

    private static NotifyIcon _tray;
    private static Timer _timer;
    private static Timer _linkTimer;
    private static Icon _iconOn;
    private static Icon _iconOff;
    private static bool _boardLinked;
    private static StatusForm _ui;
    private static bool _x1WasDown;
    private static DateTime _lastSend = DateTime.MinValue;
    private static string _hidPath;
    private static bool _enabled;
    private static bool _flashing;
    private static string _flashLog;
    private static bool _needFlash;
    private static bool _speedAfterFlash;
    private static bool _loadingKey;
    private static int _capture;
    private static byte _tapScan = 9;
    private static byte _tapSpeed = 5;
    private static string _tapName = "F";
    private static int _cmdMode = 3;
    private static int _cmdVk;
    private static int _cmdMods;
    private static byte[] _payload = new byte[] { HidCmdToggle };
    private static IntPtr _hook;
    private static LowLevelMouseProc _hookProc = Hook;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] p, uint n, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr p, ref uint size, uint headerSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pre);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr pre);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr pre, out HIDP_CAPS caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr p, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr info, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref SP_DEVICE_INTERFACE_DATA d, IntPtr det, uint detSize, out uint req, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT {
        public POINT pt;
        public int mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE {
        public ushort usUsagePage, usUsage;
        public int dwFlags;
        public IntPtr hwndTarget;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER {
        public int dwType, dwSize;
        public IntPtr hDevice, wParam;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        public ushort R0, R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11, R12, R13, R14, R15, R16;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    private sealed class RoundLamp : Control {
        private Color _fill = Color.FromArgb(180, 184, 192);
        public Color Fill {
            get { return _fill; }
            set { _fill = value; Invalidate(); }
        }
        public RoundLamp() {
            Size = new Size(18, 18);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent != null ? Parent.BackColor : UiBg);
            using (var glow = new SolidBrush(Color.FromArgb(55, _fill)))
                e.Graphics.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(_fill))
                e.Graphics.FillEllipse(b, 2, 2, Width - 5, Height - 5);
        }
    }

    private sealed class LightBtn : Button {
        private bool _hot;
        public LightBtn() {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = UiText;
            Font = FontUi;
            Cursor = Cursors.Hand;
            TabStop = false;
        }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent != null ? Parent.BackColor : UiBg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_hot) {
                using (var b = new SolidBrush(Color.FromArgb(232, 235, 240)))
                using (var path = RoundRect(r, 5))
                    e.Graphics.FillPath(b, path);
            }
            using (var path = RoundRect(r, 5))
            using (var p = new Pen(_hot ? Color.FromArgb(170, 176, 186) : UiLine))
                e.Graphics.DrawPath(p, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? UiText : UiMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private sealed class ChipField : Control {
        public ChipField() {
            Height = 26;
            Cursor = Cursors.Hand;
            Font = FontUi;
            ForeColor = UiText;
            TabStop = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnTextChanged(EventArgs e) {
            base.OnTextChanged(e);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent != null ? Parent.BackColor : UiBg);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundRect(r, 6))
            using (var b = new SolidBrush(UiChip))
            using (var p = new Pen(UiLine)) {
                e.Graphics.FillPath(b, path);
                e.Graphics.DrawPath(p, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private static GraphicsPath RoundRect(Rectangle r, int radius) {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Label MuteLabel(string text, int x, int y) {
        var l = new Label();
        l.AutoSize = true;
        l.Location = new Point(x, y);
        l.Text = text;
        l.Font = FontUi;
        l.ForeColor = UiMuted;
        l.BackColor = Color.Transparent;
        return l;
    }

    private sealed class SettingsPane : Panel {
        public readonly Button FlashBtn;
        public readonly ChipField KeyField;
        public readonly ChipField CmdField;
        public readonly TrackBar SpeedBar;
        private readonly RoundLamp _pace;
        private readonly Timer _paceTimer;
        private readonly Random _paceRng = new Random();
        private bool _paceOn;
        private static readonly Color PaceOn = Color.FromArgb(176, 122, 118);

        public SettingsPane() {
            Size = new Size(440, 88);
            BackColor = UiBg;

            Controls.Add(MuteLabel("Клавиша", 12, 12));

            KeyField = new ChipField();
            KeyField.Location = new Point(72, 8);
            KeyField.Size = new Size(72, 26);
            KeyField.Text = "F";
            KeyField.Click += delegate { BeginCapture(1); };
            Controls.Add(KeyField);

            Controls.Add(MuteLabel("Команда", 152, 12));

            CmdField = new ChipField();
            CmdField.Location = new Point(214, 8);
            CmdField.Size = new Size(130, 26);
            CmdField.Text = "обе боковые";
            CmdField.Click += delegate { BeginCapture(2); };
            Controls.Add(CmdField);

            var flash = new LightBtn();
            flash.Text = "Прошить";
            flash.Location = new Point(356, 8);
            flash.Size = new Size(72, 24);
            flash.Click += delegate { CancelCapture(); OnFlashBtn(); };
            Controls.Add(flash);
            FlashBtn = flash;

            Controls.Add(MuteLabel("Скорость", 12, 56));

            var spd = new TrackBar();
            spd.AutoSize = false;
            spd.Location = new Point(76, 50);
            spd.Size = new Size(200, 28);
            spd.Minimum = 1;
            spd.Maximum = 10;
            spd.Value = 5;
            spd.TickStyle = TickStyle.None;
            spd.BackColor = UiBg;
            spd.MouseUp += delegate { ApplySpeed(); };
            spd.KeyUp += delegate { ApplySpeed(); };
            spd.ValueChanged += delegate { KickPace(); };
            Controls.Add(spd);
            SpeedBar = spd;

            _pace = new RoundLamp();
            _pace.Size = new Size(12, 12);
            _pace.Location = new Point(282, 58);
            Controls.Add(_pace);
            _paceTimer = new Timer();
            _paceTimer.Tick += delegate { OnPaceTick(); };
            VisibleChanged += delegate {
                if (Visible) KickPace();
                else {
                    _paceTimer.Stop();
                    _paceOn = false;
                    _pace.Fill = Color.FromArgb(180, 184, 192);
                }
            };

            var drop = new LightBtn();
            drop.Text = "Снять драйвер";
            drop.Location = new Point(308, 52);
            drop.Size = new Size(120, 24);
            drop.Click += delegate { CancelCapture(); RemoveDriverClick(); };
            Controls.Add(drop);
        }

        private void KickPace() {
            if (!Visible || _pace == null) return;
            _paceOn = true;
            _pace.Fill = PaceOn;
            _paceTimer.Stop();
            _paceTimer.Interval = 80;
            _paceTimer.Start();
        }

        private void OnPaceTick() {
            if (_paceOn) {
                _paceOn = false;
                _pace.Fill = Color.FromArgb(180, 184, 192);
                int lo, hi;
                PauseRange(SpeedBar.Value, out lo, out hi);
                int span = hi - lo;
                int ms = lo;
                if (span > 0) ms = lo + (_paceRng.Next(span + 1) + _paceRng.Next(span + 1)) / 2;
                if (ms < 20) ms = 20;
                _paceTimer.Interval = ms;
                return;
            }
            _paceOn = true;
            _pace.Fill = PaceOn;
            _paceTimer.Interval = 80;
        }
    }

    private sealed class StatusForm : Form {
        public readonly RoundLamp ServiceLamp;
        public readonly RoundLamp PowerLamp;
        public readonly Label ServiceText;
        public readonly Label PowerText;
        public readonly Button FlashBtn;
        public readonly SettingsPane SettingsPane;
        private readonly LightBtn _setBtn;
        private readonly LightBtn _helpBtn;
        private const int BodyH = 80;

        public StatusForm() {
            Text = "F-Tapper — Digispark";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 40);
            ClientSize = new Size(440, BodyH);
            Icon = LoadAppIcon();
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = UiBg;
            Font = FontUi;
            ForeColor = UiText;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { OnSettingsKey(e); };

            ServiceLamp = new RoundLamp();
            ServiceLamp.Location = new Point(16, 14);
            Controls.Add(ServiceLamp);

            ServiceText = new Label();
            ServiceText.AutoSize = false;
            ServiceText.Location = new Point(42, 8);
            ServiceText.Size = new Size(386, 32);
            ServiceText.Text = "сигнал: ждёт";
            ServiceText.Font = FontUi;
            ServiceText.ForeColor = UiText;
            ServiceText.BackColor = Color.Transparent;
            Controls.Add(ServiceText);

            PowerLamp = new RoundLamp();
            PowerLamp.Location = new Point(16, 48);
            PowerLamp.Cursor = Cursors.Hand;
            PowerLamp.MouseDown += delegate { Fire(); };
            Controls.Add(PowerLamp);

            PowerText = new Label();
            PowerText.AutoSize = true;
            PowerText.Location = new Point(42, 48);
            PowerText.Text = "тапер: выкл";
            PowerText.Font = FontUi;
            PowerText.ForeColor = UiText;
            PowerText.BackColor = Color.Transparent;
            PowerText.Cursor = Cursors.Hand;
            PowerText.MouseDown += delegate { Fire(); };
            Controls.Add(PowerText);

            _setBtn = new LightBtn();
            _setBtn.Text = "Настройки";
            _setBtn.Size = new Size(84, 24);
            _setBtn.Click += delegate { ToggleSettings(); };
            Controls.Add(_setBtn);

            _helpBtn = new LightBtn();
            _helpBtn.Text = "Инструкция";
            _helpBtn.Size = new Size(92, 24);
            _helpBtn.Click += delegate { ShowHelp(); };
            Controls.Add(_helpBtn);
            PlaceSetBtn();

            SettingsPane = new SettingsPane();
            SettingsPane.Location = new Point(0, BodyH);
            SettingsPane.Visible = false;
            Controls.Add(SettingsPane);
            FlashBtn = SettingsPane.FlashBtn;
        }

        private void PlaceSetBtn() {
            _setBtn.Location = new Point(ClientSize.Width - _setBtn.Width - 12, 46);
            _helpBtn.Location = new Point(_setBtn.Left - _helpBtn.Width - 8, _setBtn.Top);
        }

        private static Form _help;

        private void ShowHelp() {
            if (_help != null && !_help.IsDisposed) {
                _help.Activate();
                return;
            }
            var f = new Form();
            f.Text = "F-Tapper — инструкция";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(440, 520);
            f.BackColor = UiBg;
            f.Font = FontUi;
            f.ForeColor = UiText;
            f.TopMost = true;
            f.ShowInTaskbar = false;
            var box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = UiBg;
            box.ForeColor = UiText;
            box.Font = FontUi;
            box.Location = new Point(16, 12);
            box.Size = new Size(408, 496);
            box.ScrollBars = ScrollBars.Vertical;
            box.TabStop = false;
            box.Text = HelpText;
            f.Controls.Add(box);
            f.FormClosed += delegate { _help = null; };
            _help = f;
            f.Show(this);
        }

        public void ShowSettings() {
            if (SettingsPane.Visible) return;
            SettingsPane.Visible = true;
            ClientSize = new Size(440, BodyH + SettingsPane.Height);
            PlaceSetBtn();
            _setBtn.Text = "Скрыть";
        }

        public void ToggleSettings() {
            if (SettingsPane.Visible) {
                CancelCapture();
                SettingsPane.Visible = false;
                ClientSize = new Size(440, BodyH);
                PlaceSetBtn();
                _setBtn.Text = "Настройки";
                return;
            }
            ShowSettings();
            CancelCapture();
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) {
                Hide();
                WindowState = FormWindowState.Normal;
            }
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            var r = new RAWINPUTDEVICE[1];
            r[0].usUsagePage = 1;
            r[0].usUsage = 2;
            r[0].dwFlags = RIDEV_INPUTSINK | RIDEV_EXINPUTSINK;
            r[0].hwndTarget = Handle;
            RegisterRawInputDevices(r, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        protected override void WndProc(ref Message m) {
            if (m.Msg == WM_INPUT) OnRawInput(m.LParam);
            base.WndProc(ref m);
        }
    }

    private static void ShowWindow() {
        if (_ui == null || _ui.IsDisposed) return;
        _ui.Show();
        _ui.WindowState = FormWindowState.Normal;
        _ui.ShowInTaskbar = true;
        _ui.Activate();
    }

    private static void ShowService(Color color, string text) {
        if (_ui == null || _ui.IsDisposed) return;
        if (_ui.InvokeRequired) {
            _ui.BeginInvoke(new Action(() => ShowService(color, text)));
            return;
        }
        _ui.ServiceLamp.Fill = color;
        _ui.ServiceText.Text = text;
    }

    private static void ShowPower() {
        if (_ui == null || _ui.IsDisposed) return;
        if (_ui.InvokeRequired) {
            _ui.BeginInvoke(new Action(ShowPower));
            return;
        }
        _ui.PowerLamp.Fill = _enabled ? Color.Lime : Color.FromArgb(180, 184, 192);
        _ui.PowerText.Text = _enabled ? "тапер: вкл" : "тапер: выкл";
    }

    private static Icon LoadAppIcon() {
        var path = Application.ExecutablePath;
        try {
            if (!string.IsNullOrEmpty(path) && path.Length > 2 && path[1] == ':') {
                var ic = Icon.ExtractAssociatedIcon(path);
                if (ic != null) return ic;
            }
        } catch { }
        try {
            var large = new IntPtr[1];
            if (!string.IsNullOrEmpty(path) && ExtractIconEx(path, 0, large, null, 1) > 0 && large[0] != IntPtr.Zero) {
                Icon clone;
                using (var tmp = Icon.FromHandle(large[0])) clone = (Icon)tmp.Clone();
                DestroyIcon(large[0]);
                return clone;
            }
        } catch { }
        return SystemIcons.Application;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint n);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr h);

    [STAThread]
    static void Main() {
        try {
            Run();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, "F-Tapper");
        }
    }

    static void Run() {
        bool created;
        using (new System.Threading.Mutex(true, @"Global\F-Tapper-single-instance", out created)) {
            if (!created) {
                MessageBox.Show("F-Tapper уже запущен.", "F-Tapper");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _ui = new StatusForm();
            LoadSettings();

            _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);

            _iconOn = MakeDotIcon(Color.Lime);
            _iconOff = MakeDotIcon(Color.OrangeRed);
            _tray = new NotifyIcon();
            _tray.Icon = _iconOff;
            _tray.Text = "F-Tapper: плата не найдена";
            _tray.Visible = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Открыть", null, delegate { ShowWindow(); });
            menu.Items.Add("Выход", null, delegate { Application.Exit(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowWindow(); };

            _timer = new Timer();
            _timer.Interval = 8;
            _timer.Tick += delegate { PollMouse(); };
            _timer.Start();

            _linkTimer = new Timer();
            _linkTimer.Interval = 1000;
            _linkTimer.Tick += delegate { UpdateTrayLink(); };
            _linkTimer.Start();
            UpdateTrayLink();

            Application.ApplicationExit += delegate {
                if (_timer != null) _timer.Stop();
                if (_linkTimer != null) _linkTimer.Stop();
                if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            };

            Application.Run(_ui);
        }
    }

    private static bool PathHasVidPid(string path, string vidPid) {
        if (string.IsNullOrEmpty(path)) return false;
        return path.ToUpperInvariant().IndexOf(vidPid, StringComparison.Ordinal) >= 0;
    }

    private static bool PathIsOurBoard(string path) {
        return PathHasVidPid(path, BoardVidPid);
    }

    private static bool PathIsBootloader(string path) {
        return PathHasVidPid(path, BootVidPid);
    }

    private static bool CmdWantsX(int xbtn) {
        if (_cmdMode == 0) return false;
        if (xbtn == XBUTTON1) return (_cmdMode & 1) != 0;
        if (xbtn == XBUTTON2) return (_cmdMode & 2) != 0;
        return false;
    }

    private static bool ModHeld(int vk) {
        if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        if (vk == 0x11) return (GetAsyncKeyState(0xA2) & 0x8000) != 0 || (GetAsyncKeyState(0xA3) & 0x8000) != 0;
        if (vk == 0x10) return (GetAsyncKeyState(0xA0) & 0x8000) != 0 || (GetAsyncKeyState(0xA1) & 0x8000) != 0;
        if (vk == 0x12) return (GetAsyncKeyState(0xA4) & 0x8000) != 0 || (GetAsyncKeyState(0xA5) & 0x8000) != 0;
        return false;
    }

    private static bool CmdModsMatch() {
        bool ctrl = ModHeld(0x11);
        bool shift = ModHeld(0x10);
        bool alt = ModHeld(0x12);
        bool wantC = (_cmdMods & (int)Keys.Control) != 0;
        bool wantS = (_cmdMods & (int)Keys.Shift) != 0;
        bool wantA = (_cmdMods & (int)Keys.Alt) != 0;
        return ctrl == wantC && shift == wantS && alt == wantA;
    }

    private static bool CmdIsDown() {
        if (_cmdMode == 0) {
            if (_cmdVk == 0) return false;
            if (!CmdModsMatch()) return false;
            return ModHeld(_cmdVk);
        }
        bool down = false;
        if ((_cmdMode & 1) != 0) down = down || ((GetAsyncKeyState(VK_XBUTTON1) & 0x8000) != 0);
        if ((_cmdMode & 2) != 0) down = down || ((GetAsyncKeyState(VK_XBUTTON2) & 0x8000) != 0);
        return down;
    }

    private static void PollMouse() {
        if (_flashing) return;
        if (_capture == 2 && _cmdMousePending != 0) {
            bool x1 = (GetAsyncKeyState(VK_XBUTTON1) & 0x8000) != 0;
            bool x2 = (GetAsyncKeyState(VK_XBUTTON2) & 0x8000) != 0;
            if (x1 && x2) {
                FinishCmdMouse(3);
                return;
            }
            if ((DateTime.UtcNow - _cmdMouseAt).TotalMilliseconds >= 500)
                FinishCmdMouse(_cmdMousePending);
            return;
        }
        if (_capture != 0) return;
        bool down = CmdIsDown();
        if (down && !_x1WasDown) Fire();
        _x1WasDown = down;
    }

    private static void OnRawInput(IntPtr hRaw) {
        if (_flashing) return;
        uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
        uint size = 0;
        GetRawInputData(hRaw, (uint)RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try {
            if (GetRawInputData(hRaw, (uint)RID_INPUT, buf, ref size, headerSize) == 0) return;
            if (Marshal.ReadInt32(buf) != RIM_TYPEMOUSE) return;
            ushort buttonFlags = (ushort)Marshal.ReadInt16(buf, (int)headerSize + 4);
            int xbtn = 0;
            if ((buttonFlags & RI_MOUSE_BUTTON_4_DOWN) != 0) xbtn = XBUTTON1;
            else if ((buttonFlags & RI_MOUSE_BUTTON_5_DOWN) != 0) xbtn = XBUTTON2;
            if (xbtn == 0) return;
            if (_capture == 2) ApplyCmdMouse(xbtn);
        } finally {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void Fire() {
        if (_flashing || _capture != 0) return;
        if ((DateTime.UtcNow - _lastSend).TotalMilliseconds < 400) return;
        string t = DateTime.Now.ToString("HH:mm:ss");
        ShowService(Color.Gold, "сигнал: " + t + " поймал");
        bool want = !_enabled;
        bool ok = SetTapper(want);
        if (ok) {
            _lastSend = DateTime.UtcNow;
            _enabled = want;
            ShowPower();
            ShowService(Color.Lime, "сигнал: " + t + " OK");
        } else {
            _lastSend = DateTime.UtcNow.AddMilliseconds(-300);
            ShowService(Color.OrangeRed, "сигнал: " + t + " FAIL");
        }
    }

    private static string SettingsPath() {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "F-Tapper", "settings.txt");
    }

    private static void SetHint(string text) {
        ShowService(Color.Gold, text);
    }

    private static void RefreshSettingFields() {
        if (_ui == null || _ui.SettingsPane == null) return;
        _ui.SettingsPane.KeyField.Text = _tapName;
        _ui.SettingsPane.CmdField.Text = CmdLabel();
        if (_ui.SettingsPane.SpeedBar != null && _ui.SettingsPane.SpeedBar.Value != _tapSpeed)
            _ui.SettingsPane.SpeedBar.Value = _tapSpeed;
    }

    private static string CmdLabel() {
        if (_cmdMode == 0) {
            if (_cmdVk == 0) return "?";
            string name = KeyName((Keys)_cmdVk);
            string p = "";
            if ((_cmdMods & (int)Keys.Control) != 0) p += "Ctrl+";
            if ((_cmdMods & (int)Keys.Shift) != 0) p += "Shift+";
            if ((_cmdMods & (int)Keys.Alt) != 0) p += "Alt+";
            return p + name;
        }
        if (_cmdMode == 1) return "Back";
        if (_cmdMode == 2) return "Forward";
        return "обе боковые";
    }

    private static string KeyName(Keys k) {
        if (k == Keys.Space) return "Space";
        if (k >= Keys.A && k <= Keys.Z) return k.ToString();
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k >= Keys.F1 && k <= Keys.F12) return k.ToString();
        return k.ToString();
    }

    private static bool IsModifierKey(Keys k) {
        return k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu ||
            k == Keys.LShiftKey || k == Keys.RShiftKey ||
            k == Keys.LControlKey || k == Keys.RControlKey ||
            k == Keys.LMenu || k == Keys.RMenu ||
            k == Keys.LWin || k == Keys.RWin ||
            k == Keys.CapsLock || k == Keys.NumLock || k == Keys.Scroll;
    }

    private static byte VkToHid(Keys k) {
        int vk = (int)k;
        if (vk >= 0x41 && vk <= 0x5A) return (byte)(vk - 0x41 + 4);
        if (vk >= 0x31 && vk <= 0x39) return (byte)(vk - 0x31 + 0x1E);
        if (vk == 0x30) return 0x27;
        if (vk >= 0x70 && vk <= 0x7B) return (byte)(vk - 0x70 + 0x3A);
        if (k == Keys.Enter) return 0x28;
        if (k == Keys.Escape) return 0x29;
        if (k == Keys.Back) return 0x2A;
        if (k == Keys.Tab) return 0x2B;
        if (k == Keys.Space) return 0x2C;
        if (k == Keys.OemMinus) return 0x2D;
        if (k == Keys.Oemplus) return 0x2E;
        if (k == Keys.OemOpenBrackets) return 0x2F;
        if (k == Keys.Oem6) return 0x30;
        if (k == Keys.Oem5) return 0x31;
        if (k == Keys.Oem1) return 0x33;
        if (k == Keys.Oem7) return 0x34;
        if (k == Keys.Oemtilde) return 0x35;
        if (k == Keys.Oemcomma) return 0x36;
        if (k == Keys.OemPeriod) return 0x37;
        if (k == Keys.OemQuestion) return 0x38;
        if (k == Keys.Insert) return 0x49;
        if (k == Keys.Home) return 0x4A;
        if (k == Keys.PageUp) return 0x4B;
        if (k == Keys.Delete) return 0x4C;
        if (k == Keys.End) return 0x4D;
        if (k == Keys.PageDown) return 0x4E;
        if (k == Keys.Right) return 0x4F;
        if (k == Keys.Left) return 0x50;
        if (k == Keys.Down) return 0x51;
        if (k == Keys.Up) return 0x52;
        return 0;
    }

    private static void BeginCapture(int mode) {
        _capture = mode;
        _cmdMousePending = 0;
        _resumeAfterCapture = _enabled;
        if (_enabled) {
            if (SetTapper(false)) {
                _enabled = false;
                ShowPower();
            } else {
                _resumeAfterCapture = false;
            }
        }
        if (_ui != null && !_ui.IsDisposed) {
            ShowWindow();
            _ui.Focus();
        }
        if (mode == 1)
            SetHint("Нажми на клавиатуре клавишу, которую должна тапать плата. Esc — отмена.");
        else
            SetHint("Боковая кнопка; для обеих — нажми Back и Forward за 0.5 с. Esc — отмена.");
    }

    private static void EndCapture() {
        _capture = 0;
        _cmdMousePending = 0;
    }

    private static void ResumeTapperIfPaused() {
        if (!_resumeAfterCapture) return;
        _resumeAfterCapture = false;
        if (SetTapper(true)) {
            _enabled = true;
            ShowPower();
        }
    }

    private static void CancelCapture() {
        EndCapture();
        ResumeTapperIfPaused();
        if (_needFlash)
            SetHint("Нужно прошить плату, чтобы применить настройки. Нажми «Прошить».");
        else
            SetHint("Нажми «Клавиша» и клавишу тапа, или «Команда» и кнопку вкл/выкл.");
    }

    private static void OnSettingsKey(KeyEventArgs e) {
        if (_capture == 0) return;
        if (e.KeyCode == Keys.Escape) {
            CancelCapture();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (IsModifierKey(e.KeyCode)) return;
        if (_capture == 1) ApplyTapKey(e.KeyCode);
        else if (_capture == 2) ApplyCmdKey(e.KeyCode, e.Modifiers);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private static void ApplyTapKey(Keys k) {
        byte hid = VkToHid(k);
        if (hid == 0) {
            SetHint("Эту клавишу плата не умеет. Нажми другую или Esc.");
            return;
        }
        _tapScan = hid;
        _tapName = KeyName(k);
        EndCapture();
        SaveSettings();
        RefreshSettingFields();
        if (PushTapKey()) {
            _needFlash = false;
            SetHint("Клавиша " + _tapName + " записана. Если плата всё ещё тапает старую — нажми «Прошить».");
        } else {
            TellNeedFlash("Клавиша " + _tapName + " сохранена, но плата её не приняла.");
        }
        ResumeTapperIfPaused();
    }

    private static void ApplyCmdKey(Keys k, Keys mods) {
        _cmdMode = 0;
        _cmdVk = (int)k;
        _cmdMods = (int)(mods & (Keys.Control | Keys.Shift | Keys.Alt));
        EndCapture();
        SaveSettings();
        RefreshSettingFields();
        DropOwnFocus();
        SetHint("Команда: " + CmdLabel() + ". Этим включай и выключай.");
        ResumeTapperIfPaused();
    }

    private static void ApplyCmdMouse(int xbtn) {
        if (_capture != 2) return;
        if (_cmdMousePending == 0) {
            _cmdMousePending = xbtn;
            _cmdMouseAt = DateTime.UtcNow;
            SetHint("Ещё нажми другую боковую для «обе», или подожди 0.5 с.");
            return;
        }
        if (_cmdMousePending != xbtn) FinishCmdMouse(3);
        else FinishCmdMouse(xbtn);
    }

    private static void FinishCmdMouse(int modeOrBtn) {
        if (_capture != 2) return;
        _cmdVk = 0;
        _cmdMods = 0;
        if (modeOrBtn == 3) _cmdMode = 3;
        else _cmdMode = (modeOrBtn == XBUTTON1) ? 1 : 2;
        EndCapture();
        SaveSettings();
        RefreshSettingFields();
        DropOwnFocus();
        SetHint("Команда: " + CmdLabel() + ". Этой кнопкой включай и выключай.");
        ResumeTapperIfPaused();
    }

    private static void LoadSettings() {
        if (_ui == null) return;
        _loadingKey = true;
        try {
            string path = SettingsPath();
            if (File.Exists(path)) {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++) {
                    string line = lines[i];
                    if (line.StartsWith("key=")) _tapName = line.Substring(4).Trim();
                    else if (line.StartsWith("scan=")) {
                        int n;
                        if (int.TryParse(line.Substring(5).Trim(), out n) && n > 0 && n < 256) _tapScan = (byte)n;
                    } else if (line.StartsWith("cmd=")) {
                        string c = line.Substring(4).Trim();
                        if (c == "back") { _cmdMode = 1; _cmdVk = 0; }
                        else if (c == "forward") { _cmdMode = 2; _cmdVk = 0; }
                        else if (c == "both") { _cmdMode = 3; _cmdVk = 0; }
                        else if (c == "key") { _cmdMode = 0; }
                    } else if (line.StartsWith("vk=")) {
                        int n;
                        if (int.TryParse(line.Substring(3).Trim(), out n) && n > 0)
                            _cmdVk = n;
                    } else if (line.StartsWith("speed=")) {
                        int n;
                        if (int.TryParse(line.Substring(6).Trim(), out n) && n >= 1 && n <= 10) _tapSpeed = (byte)n;
                    } else if (line.StartsWith("mods=")) {
                        int n;
                        if (int.TryParse(line.Substring(5).Trim(), out n)) _cmdMods = n;
                    }
                }
                if (_tapName == "E" && _tapScan == 9) _tapScan = 8;
                if (_tapName == "Space" && _tapScan == 9) _tapScan = 0x2C;
            }
            RefreshSettingFields();
        } catch {
        } finally {
            _loadingKey = false;
        }
    }

    private static void SaveSettings() {
        try {
            string dir = Path.GetDirectoryName(SettingsPath());
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string cmd = "both";
            if (_cmdMode == 1) cmd = "back";
            else if (_cmdMode == 2) cmd = "forward";
            else if (_cmdMode == 0) cmd = "key";
            File.WriteAllText(SettingsPath(),
                "key=" + _tapName + Environment.NewLine +
                "scan=" + ((int)_tapScan).ToString() + Environment.NewLine +
                "cmd=" + cmd + Environment.NewLine +
                "vk=" + _cmdVk.ToString() + Environment.NewLine +
                "mods=" + _cmdMods.ToString() + Environment.NewLine +
                "speed=" + ((int)_tapSpeed).ToString() + Environment.NewLine);
        } catch {
        }
    }

    private static bool SendCommand(byte[] cmd) {
        lock (_hidGate) {
            byte[] prev = _payload;
            _payload = cmd;
            try {
                return SendToBoard();
            } finally {
                _payload = prev;
            }
        }
    }

    private static bool? ReadTapEnabled() {
        Guid g = HidGuid;
        IntPtr list = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (list == IntPtr.Zero || list == new IntPtr(-1)) return null;
        try {
            for (uint i = 0; ; i++) {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref g, i, ref did)) break;
                uint need;
                SetupDiGetDeviceInterfaceDetail(list, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                IntPtr det = Marshal.AllocHGlobal((int)need);
                try {
                    Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref did, det, need, out need, IntPtr.Zero)) continue;
                    string path = ReadHidPath(det);
                    if (path == null || !PathIsOurBoard(path)) continue;
                    using (SafeFileHandle h = OpenHid(path)) {
                        if (h == null || h.IsInvalid) continue;
                        int featLen = 0;
                        IntPtr pre;
                        if (HidD_GetPreparsedData(h, out pre)) {
                            HIDP_CAPS caps;
                            HidP_GetCaps(pre, out caps);
                            featLen = caps.FeatureReportByteLength;
                            HidD_FreePreparsedData(pre);
                        }
                        if (featLen < 5) return null;
                        byte[] buf = new byte[featLen];
                        if (!HidD_GetFeature(h, buf, buf.Length)) continue;
                        return buf[4] != 0;
                    }
                } finally {
                    Marshal.FreeHGlobal(det);
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(list);
        }
        return null;
    }

    private static bool SetTapper(bool on) {
        if (!SendCommand(new byte[] { on ? HidCmdOn : HidCmdOff })) return false;
        System.Threading.Thread.Sleep(30);
        bool? got = ReadTapEnabled();
        if (got == null) {
            if (!SendCommand(new byte[] { HidCmdToggle })) return false;
            return true;
        }
        return got.Value == on;
    }

    private static bool PushTapKey() {
        return SendCommand(new byte[] { HidCmdSetKey, _tapScan });
    }

    private static void PauseRange(int speed, out int lo, out int hi) {
        if (speed < 1) speed = 1;
        if (speed > 10) speed = 10;
        int k = speed - 1;
        lo = 209 + (49 - 209) * k / 9;
        hi = 322 + (282 - 322) * k / 9;
        if (lo < 40) lo = 40;
        if (hi < lo) hi = lo;
    }

    private static bool PushSpeed() {
        int lo, hi;
        PauseRange(_tapSpeed, out lo, out hi);
        int loB = lo / 10;
        int hiB = (hi + 9) / 10;
        if (loB < 2) loB = 2;
        if (hiB > 255) hiB = 255;
        if (hiB < loB) hiB = loB;
        return SendCommand(new byte[] { HidCmdSetSpeed, (byte)loB, (byte)hiB });
    }

    private static void ApplySpeed() {
        if (_loadingKey || _ui == null || _ui.SettingsPane == null || _ui.SettingsPane.SpeedBar == null) return;
        int v = _ui.SettingsPane.SpeedBar.Value;
        if (v < 1) v = 1;
        if (v > 10) v = 10;
        _tapSpeed = (byte)v;
        SaveSettings();
        int lo, hi;
        PauseRange(_tapSpeed, out lo, out hi);
        if (PushSpeed())
            SetHint("Пауза " + lo + "–" + hi + " мс. Если на плате иначе — нажми «Прошить».");
        else
            TellNeedFlash("Скорость сохранена, но плата её не приняла.");
    }

    private static void ExtractResource(string name, string path) {
        using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if (src == null) throw new InvalidOperationException("нет ресурса " + name);
            using (FileStream dst = File.Create(path))
                src.CopyTo(dst);
        }
    }

    private static void TellNeedFlash(string why) {
        _needFlash = true;
        SetHint(why + " Нажми «Прошить».");
        if (_ui != null && !_ui.IsDisposed && _ui.SettingsPane != null && !_ui.SettingsPane.Visible)
            _ui.ShowSettings();
    }

    private static void TellFlashDone(bool ok, string detail) {
        if (ok) {
            _needFlash = false;
            ShowService(Color.Lime, "Прошивка закончена. Плату можно пользоваться.");
        } else {
            string text = "Прошивка не записалась. " + detail;
            if (detail.IndexOf("timed out") >= 0 || detail.IndexOf("search") >= 0)
                text = "Загрузчик не появился. Воткни плату сразу, как появится «Воткни USB обратно».";
            ShowService(Color.OrangeRed, text);
        }
        if (_ui == null || _ui.IsDisposed) return;
        ShowWindow();
        if (_ui.SettingsPane != null && !_ui.SettingsPane.Visible) _ui.ShowSettings();
        if (_ui.FlashBtn != null) _ui.FlashBtn.Enabled = true;
    }

    private static void BeginFlashLog() {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "F-Tapper");
        try { Directory.CreateDirectory(dir); } catch { }
        _flashLog = Path.Combine(dir, "flash.log");
        try {
            File.WriteAllText(_flashLog, "F-Tapper flash " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
        } catch {
            _flashLog = Path.Combine(Path.GetTempPath(), "F-Tapper-flash.log");
            try { File.WriteAllText(_flashLog, "F-Tapper flash " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine); } catch { }
        }
    }

    private static void FlashLog(string line) {
        try {
            if (string.IsNullOrEmpty(_flashLog)) return;
            File.AppendAllText(_flashLog, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);
        } catch { }
    }

    private static void FlashStep(string text) {
        if (_ui == null || _ui.IsDisposed) return;
        if (_ui.InvokeRequired) {
            _ui.Invoke(new Action(delegate { FlashStep(text); }));
            return;
        }
        _ui.FlashBtn.Enabled = false;
        ShowService(Color.Gold, text);
    }

    private static bool WaitUsb(bool want, int seconds) {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end) {
            if (UsbSeen() == want) return true;
            System.Threading.Thread.Sleep(40);
        }
        return UsbSeen() == want;
    }

    private static string RunPnputil(string args, int waitMs = 15000) {
        Process ip = new Process();
        ip.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        ip.StartInfo.Arguments = args;
        ip.StartInfo.UseShellExecute = false;
        ip.StartInfo.CreateNoWindow = true;
        ip.StartInfo.RedirectStandardOutput = true;
        ip.StartInfo.RedirectStandardError = true;
        ip.StartInfo.StandardOutputEncoding = System.Text.Encoding.Default;
        ip.StartInfo.StandardErrorEncoding = System.Text.Encoding.Default;
        var sb = new System.Text.StringBuilder();
        var se = new System.Text.StringBuilder();
        ip.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) {
            if (e.Data != null) lock (sb) sb.AppendLine(e.Data);
        };
        ip.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) {
            if (e.Data != null) lock (se) se.AppendLine(e.Data);
        };
        ip.Start();
        ip.BeginOutputReadLine();
        ip.BeginErrorReadLine();
        if (!ip.WaitForExit(waitMs)) {
            try { ip.Kill(); } catch { }
            return "";
        }
        ip.WaitForExit();
        return sb.ToString() + "\n" + se.ToString();
    }

    private const string DriverPage = "https://github.com/digistump/DigistumpArduino/releases/tag/1.6.7";

    private static bool DriverInstalled() {
        string text = RunPnputil("/enum-drivers").Replace("\0", "");
        return text.IndexOf("Digispark_Bootloader.inf", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("Digispark Bootloader", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void InstallBootDriver(string dir) {
        string drv = Path.Combine(dir, "drivers");
        Directory.CreateDirectory(Path.Combine(drv, "amd64"));
        Directory.CreateDirectory(Path.Combine(drv, "x86"));
        ExtractResource("Digispark_Bootloader.inf", Path.Combine(drv, "Digispark_Bootloader.inf"));
        ExtractResource("Digispark_Bootloader.cat", Path.Combine(drv, "Digispark_Bootloader.cat"));
        ExtractResource("drv_amd64_libusb0.sys", Path.Combine(drv, "amd64", "libusb0.sys"));
        ExtractResource("drv_amd64_libusb0.dll", Path.Combine(drv, "amd64", "libusb0.dll"));
        ExtractResource("drv_amd64_libusbK.dll", Path.Combine(drv, "amd64", "libusbK.dll"));
        ExtractResource("drv_x86_libusb0.sys", Path.Combine(drv, "x86", "libusb0.sys"));
        ExtractResource("drv_x86_libusb0.dll", Path.Combine(drv, "x86", "libusb0_x86.dll"));
        ExtractResource("drv_x86_libusbK.dll", Path.Combine(drv, "x86", "libusbK_x86.dll"));
        FlashStep("готовлю загрузчик...");
        string inf = Path.Combine(drv, "Digispark_Bootloader.inf");
        string dump = RunPnputil("/add-driver \"" + inf + "\" /install", 90000);
        string[] lines = dump.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++) {
            if (lines[i].Trim().Length > 0) FlashLog("driver " + lines[i].Trim());
        }
    }

    private static void DropOwnFocus() {
        if (_ui == null || _ui.IsDisposed) return;
        _ui.ActiveControl = null;
        IntPtr other = _ui.Handle;
        for (int n = 0; n < 30; n++) {
            other = GetWindow(other, 2);
            if (other == IntPtr.Zero) return;
            if (IsWindowVisible(other)) break;
        }
        if (other != IntPtr.Zero) SetForegroundWindow(other);
    }

    private static bool RemoveBootDriver() {
        bool removed = false;
        try {
            string text = RunPnputil("/enum-drivers").Replace("\0", "");
            string[] blocks = text.Replace("\r", "").Split(new string[] { "\n\n" }, StringSplitOptions.None);
            for (int b = 0; b < blocks.Length; b++) {
                if (blocks[b].IndexOf("Digispark_Bootloader.inf", StringComparison.OrdinalIgnoreCase) < 0
                    && blocks[b].IndexOf("Digispark Bootloader", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string[] lines = blocks[b].Split('\n');
                for (int i = 0; i < lines.Length; i++) {
                    int at = lines[i].IndexOf("oem");
                    if (at < 0) continue;
                    string rest = lines[i].Substring(at).Trim();
                    int sp = rest.IndexOf(' ');
                    if (sp < 0) sp = rest.IndexOf('\t');
                    string name = sp < 0 ? rest : rest.Substring(0, sp);
                    if (!name.StartsWith("oem") || !name.EndsWith(".inf")) continue;
                    RunPnputil("/delete-driver " + name + " /uninstall /force", 30000);
                    removed = true;
                }
            }
        } catch { }
        return removed;
    }

    private static void RemoveDriverClick() {
        if (_flashing) return;
        ShowService(Color.Gold, "снимаю драйвер...");
        System.Threading.ThreadPool.QueueUserWorkItem(delegate {
            bool had = DriverInstalled();
            bool removed = had && RemoveBootDriver();
            bool still = DriverInstalled();
            if (_ui == null || _ui.IsDisposed) return;
            _ui.BeginInvoke(new Action(delegate {
                if (!had || !still)
                    ShowService(Color.Lime, had ? "драйвер снят" : "драйвера не было");
                else
                    ShowService(Color.OrangeRed, removed ? "драйвер остался" : "драйвер не снят");
            }));
        });
    }

    private static bool EnsureTapperOff() {
        if (!_enabled) {
            FlashLog("tapper already off");
            ShowPower();
            return true;
        }
        bool stopped = SetTapper(false);
        FlashLog("tapper stop send=" + stopped);
        if (!stopped) {
            ShowService(Color.OrangeRed, "Тапер не выключился.");
            return false;
        }
        _enabled = false;
        ShowPower();
        return true;
    }

    private static string StampText(byte[] b) {
        if (b == null || b.Length < 3) return "нет";
        return b[0].ToString("X2") + b[1].ToString("X2") + b[2].ToString("X2");
    }

    private static byte[] HexStamp() {
        using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("F-Tapper.ino.hex")) {
            if (src == null) return null;
            string text;
            using (var r = new StreamReader(src)) text = r.ReadToEnd();
            var mem = new System.Collections.Generic.Dictionary<int, byte>();
            int ext = 0;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++) {
                string line = lines[n].Trim();
                if (line.Length < 11 || line[0] != ':') continue;
                int len = Convert.ToInt32(line.Substring(1, 2), 16);
                int addr = Convert.ToInt32(line.Substring(3, 4), 16);
                int typ = Convert.ToInt32(line.Substring(7, 2), 16);
                if (typ == 4 && len == 2) {
                    ext = Convert.ToInt32(line.Substring(9, 4), 16) << 16;
                    continue;
                }
                if (typ != 0) continue;
                for (int i = 0; i < len; i++)
                    mem[ext + addr + i] = Convert.ToByte(line.Substring(9 + i * 2, 2), 16);
            }
            if (mem.Count == 0) return null;
            int min = int.MaxValue, max = int.MinValue;
            foreach (int a in mem.Keys) {
                if (a < min) min = a;
                if (a > max) max = a;
            }
            for (int a = min; a <= max - 7; a++) {
                byte b0, b1, b2, b3, b4, b5, b6, b7;
                if (!mem.TryGetValue(a, out b0) || b0 != 0x46) continue;
                if (!mem.TryGetValue(a + 1, out b1) || b1 != 0x54) continue;
                if (!mem.TryGetValue(a + 2, out b2) || b2 != 0x41) continue;
                if (!mem.TryGetValue(a + 3, out b3) || b3 != 0x50) continue;
                if (!mem.TryGetValue(a + 4, out b4) || b4 != HidCmdSetKey) continue;
                if (!mem.TryGetValue(a + 5, out b5) || !mem.TryGetValue(a + 6, out b6) || !mem.TryGetValue(a + 7, out b7)) continue;
                if (b5 == 0 && b6 == 0 && b7 == 0) return null;
                return new byte[] { b5, b6, b7 };
            }
            return null;
        }
    }

    private static byte[] ReadBoardStamp() {
        Guid g = HidGuid;
        IntPtr list = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (list == IntPtr.Zero || list == new IntPtr(-1)) return null;
        try {
            for (uint i = 0; ; i++) {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref g, i, ref did)) break;
                uint need;
                SetupDiGetDeviceInterfaceDetail(list, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                IntPtr det = Marshal.AllocHGlobal((int)need);
                try {
                    Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref did, det, need, out need, IntPtr.Zero)) continue;
                    string path = ReadHidPath(det);
                    if (path == null) continue;
                    if (!PathIsOurBoard(path)) continue;
                    using (SafeFileHandle h = OpenHid(path)) {
                        if (h == null || h.IsInvalid) continue;
                        int featLen = 4;
                        IntPtr pre;
                        if (HidD_GetPreparsedData(h, out pre)) {
                            HIDP_CAPS caps;
                            HidP_GetCaps(pre, out caps);
                            if (caps.FeatureReportByteLength > 0) featLen = caps.FeatureReportByteLength;
                            HidD_FreePreparsedData(pre);
                        }
                        if (featLen < 4) continue;
                        byte[] buf = new byte[featLen];
                        if (!HidD_GetFeature(h, buf, buf.Length)) continue;
                        if (buf[1] == 0 && buf[2] == 0 && buf[3] == 0) continue;
                        return new byte[] { buf[1], buf[2], buf[3] };
                    }
                } finally {
                    Marshal.FreeHGlobal(det);
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(list);
        }
        return null;
    }

    private static void OnFlashBtn() {
        if (_flashing) return;
        ShowWindow();
        BeginFlashLog();
        FlashLog("flash click");
        ShowService(Color.Gold, "проверяю прошивку...");
        if (_ui != null && _ui.FlashBtn != null) _ui.FlashBtn.Enabled = false;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate {
            byte[] want = HexStamp();
            byte[] have = ReadBoardStamp();
            FlashLog("stamp exe " + StampText(want) + " board " + StampText(have));
            if (_ui == null || _ui.IsDisposed) return;
            _ui.BeginInvoke(new Action(delegate { AfterStampCheck(want, have); }));
        });
    }

    private static void AfterStampCheck(byte[] want, byte[] have) {
        if (_ui != null && _ui.FlashBtn != null) _ui.FlashBtn.Enabled = true;
        if (want != null && have != null && want.Length == 3 && have.Length == 3
            && want[0] == have[0] && want[1] == have[1] && want[2] == have[2]) {
            ShowService(Color.Lime, "Прошивка уже эта. Шить не нужно.");
            return;
        }
        if (!EnsureTapperOff()) return;
        ShowService(Color.Gold, "проверяю драйвер...");
        if (_ui != null && _ui.FlashBtn != null) _ui.FlashBtn.Enabled = false;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate {
            bool has = false;
            try { has = DriverInstalled(); }
            catch (Exception ex) { FlashLog("driver check " + ex.Message); }
            FlashLog("driver present " + has);
            if (_ui == null || _ui.IsDisposed) return;
            _ui.BeginInvoke(new Action(delegate { AfterDriverCheck(has); }));
        });
    }

    private static void AfterDriverCheck(bool has) {
        if (_ui == null || _ui.IsDisposed) return;
        _ui.Activate();
        if (has) {
            ShowService(Color.Gold, "драйвер есть");
            if (_ui.FlashBtn != null) _ui.FlashBtn.Enabled = true;
            FlashBoard();
            return;
        }
        ShowService(Color.Gold, "Драйвера нет. Ставлю из программы...");
        System.Threading.ThreadPool.QueueUserWorkItem(delegate {
            bool ok = false;
            string drvDir = null;
            try {
                drvDir = Path.Combine(Path.GetTempPath(), "F-Tapper-drv-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(drvDir);
                InstallBootDriver(drvDir);
                ok = DriverInstalled();
            } catch (Exception ex) {
                FlashLog("driver " + ex.Message);
            } finally {
                if (drvDir != null) {
                    try { Directory.Delete(drvDir, true); } catch { }
                }
            }
            if (_ui == null || _ui.IsDisposed) return;
            bool installed = ok;
            _ui.BeginInvoke(new Action(delegate {
                ResetFlashBtn();
                if (installed)
                    ShowService(Color.Gold, "Драйвер поставлен. Нажми «Прошить» ещё раз.");
                else
                    ShowService(Color.OrangeRed, "Драйвер не встал. Нажми «Прошить» ещё раз.");
            }));
        });
    }

    private static void ResetFlashBtn() {
        if (_ui == null || _ui.IsDisposed || _ui.FlashBtn == null) return;
        if (_ui.InvokeRequired) {
            _ui.BeginInvoke(new Action(ResetFlashBtn));
            return;
        }
        _ui.FlashBtn.Text = "Прошить";
        _ui.FlashBtn.Enabled = true;
    }

    private static void KillLeftoverMicronucleus() {
        Process[] old = Process.GetProcessesByName("micronucleus");
        for (int i = 0; i < old.Length; i++) {
            try {
                string path = null;
                try { path = old[i].MainModule.FileName; } catch { }
                if (string.IsNullOrEmpty(path)) continue;
                if (path.IndexOf("F-Tapper-flash", StringComparison.OrdinalIgnoreCase) < 0) continue;
                old[i].Kill();
                FlashLog("killed leftover micronucleus pid=" + old[i].Id);
            } catch { }
            finally {
                try { old[i].Dispose(); } catch { }
            }
        }
    }

    private static void FlashBoard() {
        if (_flashing) return;
        _flashing = true;
        _boardLinked = false;
        _hidPath = null;
        _speedAfterFlash = true;
        BeginFlashLog();
        FlashLog("log " + _flashLog);
        System.Threading.ThreadPool.QueueUserWorkItem(delegate {
            Process p = null;
            string dir = null;
            try {
                dir = Path.Combine(Path.GetTempPath(), "F-Tapper-flash-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                if (_enabled) {
                    bool stopped = SetTapper(false);
                    FlashLog("tapper stopped send=" + stopped);
                    if (!stopped) {
                        _ui.BeginInvoke(new Action(delegate {
                            TellFlashDone(false, "Тапер не выключился.");
                        }));
                        return;
                    }
                    _enabled = false;
                    ShowPower();
                } else {
                    FlashLog("tapper already off");
                }
                KillLeftoverMicronucleus();
                string mn = Path.Combine(dir, "micronucleus.exe");
                string hex = Path.Combine(dir, "F-Tapper.ino.hex");
                ExtractResource("micronucleus.exe", mn);
                ExtractResource("libusb0.dll", Path.Combine(dir, "libusb0.dll"));
                ExtractResource("libusbK.dll", Path.Combine(dir, "libusbK.dll"));
                ExtractResource("F-Tapper.ino.hex", hex);
                FlashLog("hex bytes " + new FileInfo(hex).Length);

                FlashLog("usb before unplug " + UsbSeen());
                FlashStep("Выдерни USB платы.");
                if (UsbSeen() && !WaitUsb(false, 120)) {
                    FlashLog("unplug timeout usb=" + UsbSeen());
                    _ui.BeginInvoke(new Action(delegate { TellFlashDone(false, "Плата так и не отключилась."); }));
                    return;
                }
                FlashLog("usb after unplug " + UsbSeen());
                System.Threading.Thread.Sleep(400);

                string log = "";
                object gate = new object();
                p = new Process();
                p.StartInfo.FileName = mn;
                p.StartInfo.Arguments = "--no-ansi --run --timeout 60 \"" + hex + "\"";
                p.StartInfo.WorkingDirectory = dir;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                DataReceivedEventHandler onLine = delegate(object s, DataReceivedEventArgs e) {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    FlashLog("mn " + e.Data);
                    lock (gate) log += e.Data + "\n";
                    string line = e.Data;
                    if (line.IndexOf("%") >= 0 || line.IndexOf("Device") >= 0 || line.IndexOf("ras") >= 0)
                        FlashStep("шью...");
                };
                p.OutputDataReceived += onLine;
                p.ErrorDataReceived += onLine;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                FlashLog("micronucleus pid=" + p.Id);
                FlashStep("Воткни USB обратно.");

                if (!p.WaitForExit(70000)) {
                    try { p.Kill(); } catch { }
                    FlashLog("killed on wait");
                    _ui.BeginInvoke(new Action(delegate { TellFlashDone(false, "Не дождался платы."); }));
                    return;
                }
                p.WaitForExit();
                string text;
                lock (gate) text = log;
                FlashLog("exit " + p.ExitCode);
                bool wrote = text.IndexOf("Micronucleus done") >= 0 || text.IndexOf("Starting the user app") >= 0;
                if (p.ExitCode == 0 || wrote)
                    _ui.BeginInvoke(new Action(delegate { TellFlashDone(true, ""); }));
                else {
                    string why = "код " + p.ExitCode;
                    string[] lines = text.Replace("\r", "").Split('\n');
                    for (int i = lines.Length - 1; i >= 0; i--) {
                        if (lines[i].Trim().Length > 0) { why = lines[i].Trim(); break; }
                    }
                    FlashLog("fail " + why);
                    _ui.BeginInvoke(new Action(delegate { TellFlashDone(false, why); }));
                }
            } catch (Exception ex) {
                FlashLog("error " + ex);
                if (p != null && !p.HasExited) {
                    try { p.Kill(); } catch { }
                }
                string msg = ex.Message;
                if (_ui != null)
                    _ui.BeginInvoke(new Action(delegate { TellFlashDone(false, msg); }));
            } finally {
                if (p != null) {
                    try { p.Dispose(); } catch { }
                }
                if (dir != null) {
                    try { Directory.Delete(dir, true); } catch { }
                }
                _flashing = false;
                ResetFlashBtn();
                FlashLog("flash finished");
            }
        });
    }

    private static Icon MakeDotIcon(Color fill) {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var b = new SolidBrush(fill))
                g.FillEllipse(b, 0, 0, 15, 15);
            using (var p = new Pen(Color.FromArgb(90, 0, 0, 0)))
                g.DrawEllipse(p, 0, 0, 15, 15);
            using (var font = new Font("Tahoma", 8.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat(StringFormat.GenericTypographic)) {
                sf.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
                SizeF sz = g.MeasureString("TAP", font, int.MaxValue, sf);
                float x = (16f - sz.Width) / 2f;
                float y = (16f - sz.Height) / 2f + 0.5f;
                using (var tb = new SolidBrush(Color.Black))
                    g.DrawString("TAP", font, tb, x, y, sf);
            }
        }
        IntPtr h = bmp.GetHicon();
        Icon clone;
        using (var tmp = Icon.FromHandle(h))
            clone = (Icon)tmp.Clone();
        DestroyIcon(h);
        bmp.Dispose();
        return clone;
    }

    private static bool BoardPresent() {
        Guid g = HidGuid;
        IntPtr list = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (list == IntPtr.Zero || list == new IntPtr(-1)) {
            _hidPath = null;
            return false;
        }
        try {
            for (uint i = 0; ; i++) {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref g, i, ref did)) break;
                uint need;
                SetupDiGetDeviceInterfaceDetail(list, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                IntPtr det = Marshal.AllocHGlobal((int)need);
                try {
                    Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref did, det, need, out need, IntPtr.Zero)) continue;
                    string path = ReadHidPath(det);
                    if (path == null) continue;
                    if (PathIsOurBoard(path)) return true;
                } finally {
                    Marshal.FreeHGlobal(det);
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(list);
        }
        _hidPath = null;
        return false;
    }

    private static bool UsbSeen() {
        if (BoardPresent()) return true;
        Guid g = UsbGuid;
        IntPtr list = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (list == IntPtr.Zero || list == new IntPtr(-1)) return false;
        try {
            for (uint i = 0; ; i++) {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref g, i, ref did)) break;
                uint need;
                SetupDiGetDeviceInterfaceDetail(list, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                if (need < 8) continue;
                IntPtr det = Marshal.AllocHGlobal((int)need);
                try {
                    Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref did, det, need, out need, IntPtr.Zero)) continue;
                    string a = Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 4));
                    string b = Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 8));
                    string s = ((a ?? "") + " " + (b ?? "")).ToUpperInvariant();
                    if (PathIsOurBoard(s) || PathIsBootloader(s)) return true;
                } finally {
                    Marshal.FreeHGlobal(det);
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(list);
        }
        return false;
    }

    private static void UpdateTrayLink() {
        if (_flashing || _tray == null) return;
        bool on = BoardPresent();
        if (on && !_boardLinked) {
            _linkAt = DateTime.UtcNow;
            _needEnableSync = _enabled;
            PushTapKey();
            bool speedOk = PushSpeed();
            if (_speedAfterFlash) {
                int lo, hi;
                PauseRange(_tapSpeed, out lo, out hi);
                FlashLog("speed after flash send=" + speedOk + " pause " + lo + "-" + hi + " slider " + _tapSpeed);
                _speedAfterFlash = false;
            }
        }
        if (on && _needEnableSync && (DateTime.UtcNow - _linkAt).TotalMilliseconds >= 2500) {
            _needEnableSync = false;
            if (_enabled) {
                bool synced = SetTapper(true);
                FlashLog("enable sync after arm send=" + synced);
                if (!synced) {
                    _enabled = false;
                    ShowPower();
                }
            }
        }
        if (!on) _needEnableSync = false;
        if (on == _boardLinked && _tray.Icon != null) return;
        _boardLinked = on;
        _tray.Icon = on ? _iconOn : _iconOff;
        _tray.Text = on ? "F-Tapper: плата на связи" : "F-Tapper: плата не найдена";
    }

    private static bool SendToBoard() {
        if (!string.IsNullOrEmpty(_hidPath) && PathIsOurBoard(_hidPath) && TrySendPath(_hidPath)) return true;
        Guid g = HidGuid;
        IntPtr list = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (list == IntPtr.Zero || list == new IntPtr(-1)) return false;
        try {
            for (uint i = 0; ; i++) {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                if (!SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref g, i, ref did)) break;
                uint need;
                SetupDiGetDeviceInterfaceDetail(list, ref did, IntPtr.Zero, 0, out need, IntPtr.Zero);
                IntPtr det = Marshal.AllocHGlobal((int)need);
                try {
                    Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref did, det, need, out need, IntPtr.Zero)) continue;
                    string path = ReadHidPath(det);
                    if (path == null) continue;
                    if (!PathIsOurBoard(path)) continue;
                    if (TrySendPath(path)) {
                        _hidPath = path;
                        return true;
                    }
                } finally {
                    Marshal.FreeHGlobal(det);
                }
            }
        } finally {
            SetupDiDestroyDeviceInfoList(list);
        }
        return false;
    }

    private static string ReadHidPath(IntPtr det) {
        string[] cand = {
            Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 4)),
            Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 8))
        };
        for (int i = 0; i < cand.Length; i++) {
            string s = cand[i];
            if (string.IsNullOrEmpty(s)) continue;
            if (s.StartsWith("?")) s = @"\\" + s;
            if (s.IndexOf("HID#", StringComparison.OrdinalIgnoreCase) < 0 &&
                s.IndexOf(@"\HID", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!s.StartsWith(@"\\")) s = @"\\?\" + s.TrimStart('\\', '?');
            return s;
        }
        return null;
    }

    private static SafeFileHandle OpenHid(string path) {
        uint[] acc = { 0, GENERIC_WRITE, GENERIC_READ | GENERIC_WRITE };
        for (int a = 0; a < acc.Length; a++) {
            SafeFileHandle h = CreateFile(path, acc[a], FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h != null && !h.IsInvalid) return h;
            if (h != null) h.Dispose();
        }
        return null;
    }

    private static bool TrySendPath(string path) {
        using (SafeFileHandle h = OpenHid(path)) {
            if (h == null || h.IsInvalid) return false;
            int outLen = 1, featLen = 0;
            IntPtr pre;
            if (HidD_GetPreparsedData(h, out pre)) {
                HIDP_CAPS caps;
                HidP_GetCaps(pre, out caps);
                if (caps.OutputReportByteLength > 0) outLen = caps.OutputReportByteLength;
                featLen = caps.FeatureReportByteLength;
                HidD_FreePreparsedData(pre);
            }
            if (SendOne(h, true, outLen)) return true;
            if (featLen > 0 && SendOne(h, false, featLen)) return true;
            return false;
        }
    }

    private static bool SendOne(SafeFileHandle h, bool output, int reportLen) {
        int n = Math.Max(Math.Max(1, reportLen), _payload.Length);
        byte[] withId = new byte[n + 1];
        withId[0] = 0;
        Array.Copy(_payload, 0, withId, 1, _payload.Length);
        byte[] raw = new byte[n];
        Array.Copy(_payload, 0, raw, 0, _payload.Length);
        if (output) {
            if (HidD_SetOutputReport(h, withId, withId.Length)) return true;
            if (HidD_SetOutputReport(h, raw, raw.Length)) return true;
        } else {
            if (HidD_SetFeature(h, withId, withId.Length)) return true;
            if (HidD_SetFeature(h, raw, raw.Length)) return true;
        }
        return false;
    }

    private static IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0 && !_flashing) {
            int msg = wParam.ToInt32();
            if (msg == WM_XBUTTONDOWN) {
                var hs = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int xbtn = (hs.mouseData >> 16) & 0xFFFF;
                if ((xbtn == XBUTTON1 || xbtn == XBUTTON2) && _capture == 2)
                    ApplyCmdMouse(xbtn);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
