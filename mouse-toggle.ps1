# Боковая кнопка Back шлёт команду напрямую на плату. F нажимает сама плата.
# Окно не закрывай.

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
  Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-STA','-File', $PSCommandPath) -Wait
  exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

public static class BoardTapper {
    private const int WH_MOUSE_LL = 14;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int XBUTTON1 = 1;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const int DIGCF_PRESENT = 2;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private static readonly Guid HidGuid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelMouseProc _proc = Hook;
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT {
        public POINT pt; public int mouseData; public int flags; public int time; public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA {
        public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
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

    public static bool SendToBoard() {
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
                    string up = path.ToUpperInvariant();
                    if (up.IndexOf("VID_16C0") < 0) continue;
                    if (TrySendPath(path)) return true;
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
        string[] cand = new string[] {
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
        uint[] acc = new uint[] { 0, GENERIC_WRITE, GENERIC_READ | GENERIC_WRITE };
        uint[] flags = new uint[] { 0 };
        for (int a = 0; a < acc.Length; a++) {
            for (int f = 0; f < flags.Length; f++) {
                SafeFileHandle h = CreateFile(path, acc[a], FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, flags[f], IntPtr.Zero);
                if (h != null && !h.IsInvalid) return h;
                if (h != null) h.Dispose();
            }
        }
        return null;
    }

    private static bool TrySendPath(string path) {
        using (SafeFileHandle h = OpenHid(path)) {
            if (h == null || h.IsInvalid) {
                Console.WriteLine("open fail " + path);
                Console.Out.Flush();
                return false;
            }
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
            Console.WriteLine("send fail out=" + outLen + " feat=" + featLen + " " + path);
            Console.Out.Flush();
            return false;
        }
    }

    private static bool SendOne(SafeFileHandle h, bool output, int reportLen) {
        byte[] withId = new byte[reportLen + 1];
        withId[0] = 0;
        withId[1] = 2;
        byte[] raw = new byte[Math.Max(1, reportLen)];
        raw[0] = 2;
        if (output) {
            if (HidD_SetOutputReport(h, withId, withId.Length)) return true;
            if (HidD_SetOutputReport(h, raw, raw.Length)) return true;
        } else {
            if (HidD_SetFeature(h, withId, withId.Length)) return true;
            if (HidD_SetFeature(h, raw, raw.Length)) return true;
        }
        return false;
    }

    private static volatile int _pending = 0;
    private static Timer _timer;

    public static void Toggle() {
        if (SendToBoard()) {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " USB cmd OK");
        } else {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " USB cmd FAIL");
        }
        Console.Out.Flush();
    }

    public static void Run() {
        using (Process cur = Process.GetCurrentProcess())
        using (ProcessModule mod = cur.MainModule) {
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        }
        if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        Console.WriteLine("Board tapper ready. Side button Back = on/off. Ctrl+C quit.");
        _timer = new Timer();
        _timer.Interval = 30;
        _timer.Tick += delegate {
            if (_pending == 0) return;
            _pending = 0;
            Toggle();
        };
        _timer.Start();
        Application.Run();
    }

    private static IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            int msg = wParam.ToInt32();
            if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP) {
                MSLLHOOKSTRUCT hs = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int xbtn = (hs.mouseData >> 16) & 0xFFFF;
                if (xbtn == XBUTTON1) {
                    if (msg == WM_XBUTTONDOWN) _pending = 1;
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
"@ -ReferencedAssemblies System.Windows.Forms

[BoardTapper]::Run()
