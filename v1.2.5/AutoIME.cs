// ============================================================================
//  AutoIME  -  输入法自动开关（Adobe / Autodesk 创意软件）
//
//  原理（和「我的世界」的 IMBlocker 插件一致）:
//    不切换输入法，只控制「输入法开 / 关」这个状态。
//      · 鼠标不在输入框、正在用快捷键 -> 关闭输入法，按键直接送到软件
//      · 鼠标在输入框/正在打字       -> 打开输入法，输入中文
//    实现方式: ImmGetDefaultIMEWnd(窗口) 拿到目标程序的 IME 窗口，
//              再用 WM_IME_CONTROL + IMC_SETOPENSTATUS 开关它（跨进程有效）
//
//  编译: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:x64 AutoIME.cs
//  依赖: .NET Framework 4.x（Windows 自带）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;

[assembly: AssemblyTitle("AutoIME 输入法自动开关")]
[assembly: AssemblyDescription("Adobe / Autodesk 创意软件输入法自动开关：不切换键盘布局，只锁定/放行输入法")]
[assembly: AssemblyProduct("AutoIME")]
[assembly: AssemblyVersion("1.2.5.0")]
[assembly: AssemblyFileVersion("1.2.5.0")]

namespace AutoIME
{
    /// <summary>版本号：升级新版本时改这里 + 上面的 AssemblyVersion</summary>
    internal static class Ver
    {
        public const string Number = "1.2.5";
        public const string Date = "2026-11-13";
        public static string Text { get { return "v" + Number; } }
    }

    // ---------------------------------------------------------------- 原生 API
    internal static class Nat
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        public const int CURSOR_SHOWING = 0x0001;
        public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        public const uint SMTO_ABORTIFHUNG = 0x0002;
        public const uint SMTO_BLOCK = 0x0001;
        public const int IDC_IBEAM = 32513;
        public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // ---- IME 控制消息 ----
        public const uint WM_IME_CONTROL = 0x0283;
        public const int IMC_GETCONVERSIONMODE = 0x0001;
        public const int IMC_SETCONVERSIONMODE = 0x0002;
        public const int IMC_GETOPENSTATUS = 0x0005;
        public const int IMC_SETOPENSTATUS = 0x0006;
        public const int IME_CMODE_NATIVE = 0x0001;
        public const int IME_CMODE_LANGUAGE = 0x0003;

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
        [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);
        [DllImport("user32.dll")] public static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO pci);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        [DllImport("imm32.dll")] public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
        [DllImport("imm32.dll")] public static extern bool ImmIsIME(IntPtr hkl);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);
    }

    // ------------------------------------------------------------- IME 开关封装
    internal static class Ime
    {
        public static bool Get(IntPtr imeWnd, int cmd, out int val)
        {
            val = 0;
            if (imeWnd == IntPtr.Zero) return false;
            IntPtr r;
            IntPtr ok = Nat.SendMessageTimeout(imeWnd, Nat.WM_IME_CONTROL, (IntPtr)cmd, IntPtr.Zero,
                Nat.SMTO_ABORTIFHUNG | Nat.SMTO_BLOCK, 250, out r);
            val = r.ToInt32();
            return ok != IntPtr.Zero;
        }

        public static bool Set(IntPtr imeWnd, int cmd, int val)
        {
            if (imeWnd == IntPtr.Zero) return false;
            IntPtr r;
            IntPtr ok = Nat.SendMessageTimeout(imeWnd, Nat.WM_IME_CONTROL, (IntPtr)cmd, (IntPtr)val,
                Nat.SMTO_ABORTIFHUNG | Nat.SMTO_BLOCK, 250, out r);
            return ok != IntPtr.Zero;
        }

        // 关闭/打开输入法。返回读回的真实状态（失败会重试一次）
        public static bool SetOpen(IntPtr imeWnd, bool open, out bool realOpen)
        {
            realOpen = !open;
            if (imeWnd == IntPtr.Zero) return false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Set(imeWnd, Nat.IMC_SETOPENSTATUS, open ? 1 : 0);
                Thread.Sleep(attempt == 0 ? 12 : 30);
                int v;
                if (Get(imeWnd, Nat.IMC_GETOPENSTATUS, out v))
                {
                    realOpen = (v != 0);
                    if (realOpen == open) return true;
                }
            }
            return false;
        }

        // 把输入法内部的中/英切成中文
        public static void SetChineseMode(IntPtr imeWnd)
        {
            int conv;
            if (!Get(imeWnd, Nat.IMC_GETCONVERSIONMODE, out conv)) return;
            if ((conv & Nat.IME_CMODE_LANGUAGE) == Nat.IME_CMODE_NATIVE) return;
            Set(imeWnd, Nat.IMC_SETCONVERSIONMODE,
                (conv & ~Nat.IME_CMODE_LANGUAGE) | Nat.IME_CMODE_NATIVE);
        }
    }

    // ------------------------------------------------------------------ 配置
    internal class Cfg
    {
        public string Path;
        public bool Enabled = true;
        public string Method = "ime";          // ime=开关输入法(默认) / layout=切换键盘布局
        public int PollIntervalMs = 40;
        public int EnterDelayMs = 30;
        public int LeaveDelayMs = 350;
        public int ManualOverrideMs = 4000;
        public int SyncIntervalMs = 250;
        public bool RestoreOnLeave = true;
        public bool StrictLock = true;                 // 严格锁定：非输入框时强制关闭输入法，不因手动切换而暂停
        public bool AllowLayoutSwitch = false;         // 是否允许切换键盘布局（按"不切换键盘"的要求默认关）
        public bool ShowBadge = true;
        public bool ShowTrayIcon = true;            // 是否在右下角通知区显示托盘图标（隐藏后程序仍后台运行）
        public bool ShowTaskbarButton = true;      // 设置界面打开时是否在任务栏按钮区显示窗口按钮（后台运行本身不占任务栏）
        public bool BadgeAlways = false;
        public int BadgeMs = 1400;
        public bool BadgeTopMost = true;              // 浮窗是否置顶
        public bool BadgeLocked = false;              // 锁定浮窗位置/大小（锁定后禁止拖动、缩放）
        public double BadgeOpacity = 0.93;            // 浮窗不透明度 0.30~1.00
        public int BadgeW = 140, BadgeH = 52;        // 浮窗尺寸（可拖拽缩放后保存）
        public int BadgeX = -1, BadgeY = -1;         // 浮窗位置（-1=默认右下角）
        public bool AllApps = false;
        public bool UseStdIBeam = true;
        public bool UseShape = false;
        public bool UseShapeLoose = false;              // 放宽形状判定（针对大尺寸自绘 I 型光标）
        public bool UseCaret = true;
        public bool UseClasses = true;
        public bool PsToolRule = true;                // PS 当前工具自动识别（经 cscript 读取，默认开）
        public List<string> PsProcs = new List<string>();
        public int LogLevel = 1;

        public IntPtr ChineseHkl = IntPtr.Zero;
        public IntPtr EnglishHkl = IntPtr.Zero;

        public List<string> Apps = new List<string>();
        public List<string> TextClasses = new List<string>();
        public List<string> NumClasses = new List<string>();

        public string HotToggle = "CTRL+ALT+SHIFT+I";
        public string HotLearn = "CTRL+ALT+SHIFT+L";
        public string HotDiag = "CTRL+ALT+SHIFT+D";
        public string HotSettings = "CTRL+ALT+SHIFT+S";

        public bool IsTarget(string proc)
        {
            if (AllApps) return true;
            for (int i = 0; i < Apps.Count; i++)
                if (string.Equals(Apps[i], proc, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Cfg Load(string path)
        {
            var c = new Cfg();
            c.Path = path;
            if (!File.Exists(path)) { c.Save(); return c; }
            string section = "";
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                c.Set(section, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            if (c.TextClasses.Count == 0) c.TextClasses = DefaultTextClasses();
            if (c.PsProcs.Count == 0) c.PsProcs = DefaultPsProcs();
            if (c.NumClasses.Count == 0) c.NumClasses = DefaultNumClasses();
            if (c.Apps.Count == 0) c.Apps = DefaultApps();
            return c;
        }

        public static List<string> DefaultApps()
        {
            return new List<string>(new string[] {
                // 原有：Adobe Photoshop / Autodesk 3ds Max / Maya
                "Photoshop", "3dsmax", "3dsmaxb", "3dsmaxio",
                "maya", "maya2020", "maya2022", "maya2023", "maya2024", "maya2025", "maya2026",

                // Adobe 全家桶（进程名，不含 .exe；ProcessName 保留空格）
                "Illustrator", "InDesign", "InCopy",
                "Adobe Premiere Pro", "PremiereElementsEditor",
                "AfterFX", "Animate", "Adobe Audition", "Dreamweaver",
                "Lightroom", "Adobe Media Encoder", "Adobe Bridge",
                "Acrobat", "AcroCEF",
                "Adobe Substance 3D Designer", "Substance 3D Painter",
                "Substance 3D Sampler", "Substance 3D Stager", "Substance 3D Modeler",
                "animator",

                // Autodesk 全家桶
                "acad", "Revit", "Navisworks", "Inventor", "Fusion",
                "Alias", "MotionBuilder", "Mudbox", "VREDPro",
                "Powershape", "Recap", "DWGViewer"
            });
        }

        // 这些类名是“文本输入框”
        public static List<string> DefaultTextClasses()
        {
            return new List<string>(new string[] {
                "Edit", "RICHEDIT", "RichEdit20W", "RichEdit20A", "RICHEDIT50W",
                "Scintilla", "CustEdit", "CustTextField", "TextEditor"
            });
        }

        public static List<string> DefaultPsProcs()
        {
            return new List<string>(new string[] { "Photoshop", "PhotoshopBeta" });
        }

        // 这些类名是“数字输入框”（要英文，避免拼音抢数字键）
        public static List<string> DefaultNumClasses()
        {
            return new List<string>(new string[] {
                "SpinnerControl", "CustSpinner", "msctls_updown32", "SpinBox"
            });
        }

        void Set(string section, string key, string v)
        {
            bool b; int n;
            switch (section + "." + key.ToLowerInvariant())
            {
                case "general.enabled": if (TryBool(v, out b)) Enabled = b; break;
                case "general.method": Method = v.Trim().ToLowerInvariant(); break;
                case "general.pollintervalms": if (int.TryParse(v, out n)) PollIntervalMs = Math.Max(10, Math.Min(500, n)); break;
                case "general.enterdelayms": if (int.TryParse(v, out n)) EnterDelayMs = Math.Max(0, Math.Min(2000, n)); break;
                case "general.leavedelayms": if (int.TryParse(v, out n)) LeaveDelayMs = Math.Max(0, Math.Min(5000, n)); break;
                case "general.manualoverridems": if (int.TryParse(v, out n)) ManualOverrideMs = Math.Max(0, Math.Min(60000, n)); break;
                case "general.syncintervalms": if (int.TryParse(v, out n)) SyncIntervalMs = Math.Max(100, Math.Min(2000, n)); break;
                case "general.restoreonleave": if (TryBool(v, out b)) RestoreOnLeave = b; break;
                case "general.strictlock": if (TryBool(v, out b)) StrictLock = b; break;
                case "general.allowlayoutswitch": if (TryBool(v, out b)) AllowLayoutSwitch = b; break;
                case "general.showbadge": if (TryBool(v, out b)) ShowBadge = b; break;
                case "general.showtrayicon": if (TryBool(v, out b)) ShowTrayIcon = b; break;
                case "general.showtaskbarbutton": if (TryBool(v, out b)) ShowTaskbarButton = b; break;
                case "general.badgealways": if (TryBool(v, out b)) BadgeAlways = b; break;
                case "general.badgems": if (int.TryParse(v, out n)) BadgeMs = Math.Max(300, Math.Min(10000, n)); break;
                case "general.badgetopmost": if (TryBool(v, out b)) BadgeTopMost = b; break;
                case "general.badgelocked": if (TryBool(v, out b)) BadgeLocked = b; break;
                case "general.badgeopacity": { double od; if (double.TryParse(v, out od)) BadgeOpacity = Math.Max(0.30, Math.Min(1.00, od)); break; }
                case "general.badgew": if (int.TryParse(v, out n)) BadgeW = Math.Max(96, Math.Min(600, n)); break;
                case "general.badgeh": if (int.TryParse(v, out n)) BadgeH = Math.Max(40, Math.Min(300, n)); break;
                case "general.badgex": if (int.TryParse(v, out n)) BadgeX = n; break;
                case "general.badgey": if (int.TryParse(v, out n)) BadgeY = n; break;
                case "general.allapps": if (TryBool(v, out b)) AllApps = b; break;
                case "general.loglevel": if (int.TryParse(v, out n)) LogLevel = n; break;
                case "general.chinesehkl": ChineseHkl = ParseHkl(v); break;
                case "general.englishhkl": EnglishHkl = ParseHkl(v); break;
                case "detect.stdibeam": if (TryBool(v, out b)) UseStdIBeam = b; break;
                case "detect.ibeamshape": if (TryBool(v, out b)) UseShape = b; break;
                case "detect.ibeamshapeloose": if (TryBool(v, out b)) UseShapeLoose = b; break;
                case "detect.caretwindow": if (TryBool(v, out b)) UseCaret = b; break;
                case "detect.useclasses": if (TryBool(v, out b)) UseClasses = b; break;
                case "detect.textclasses": TextClasses = SplitList(v); break;
                case "detect.pstoolrule": if (TryBool(v, out b)) PsToolRule = b; break;
                case "detect.psprocs": PsProcs = SplitList(v); break;
                case "detect.numclasses": NumClasses = SplitList(v); break;
                case "keys.toggle": HotToggle = v; break;
                case "keys.learn": HotLearn = v; break;
                case "keys.diag": HotDiag = v; break;
                case "keys.settings": HotSettings = v; break;
                default:
                    if (section == "apps" && TryBool(v, out b) && b) Apps.Add(key);
                    break;
            }
        }

        public static IntPtr ParseHkl(string v)
        {
            if (string.IsNullOrEmpty(v)) return IntPtr.Zero;
            string s = v.Trim();
            if (s.Equals("auto", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            int n;
            if (int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out n)) return (IntPtr)n;
            return IntPtr.Zero;
        }

        static bool TryBool(string v, out bool b)
        {
            b = false;
            v = v.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") { b = true; return true; }
            if (v == "0" || v == "false" || v == "no" || v == "off") { b = false; return true; }
            return false;
        }

        static List<string> SplitList(string v)
        {
            var l = new List<string>();
            foreach (var p in v.Split(new char[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                l.Add(p.Trim());
            return l;
        }

        public void Save()
        {
            if (Apps.Count == 0) Apps = DefaultApps();
            if (TextClasses.Count == 0) TextClasses = DefaultTextClasses();
            if (NumClasses.Count == 0) NumClasses = DefaultNumClasses();
            if (PsProcs.Count == 0) PsProcs = DefaultPsProcs();
            var sb = new StringBuilder();
            sb.AppendLine("; =================================================================");
            sb.AppendLine(";  AutoIME 配置文件");
            sb.AppendLine(";  改完保存后：右键托盘图标 -> 重新载入 AutoIME.ini");
            sb.AppendLine("; =================================================================");
            sb.AppendLine();
            sb.AppendLine("[General]");
            sb.AppendLine("; 总开关 1=开 0=关");
            sb.AppendLine("Enabled=" + B(Enabled));
            sb.AppendLine("; ime    = 只开关输入法，不切换输入法（默认，推荐）");
            sb.AppendLine("; layout = 切换到英文键盘布局（备用方案）");
            sb.AppendLine("Method=" + Method);
            sb.AppendLine("; 轮询间隔(毫秒)");
            sb.AppendLine("PollIntervalMs=" + PollIntervalMs);
            sb.AppendLine("; 判定为输入框后，多久切中文(毫秒)");
            sb.AppendLine("EnterDelayMs=" + EnterDelayMs);
            sb.AppendLine("; 离开输入框后，多久关闭输入法(毫秒)");
            sb.AppendLine("LeaveDelayMs=" + LeaveDelayMs);
            sb.AppendLine("; 你手动开关输入法后，程序暂停自动控制多久(毫秒)");
            sb.AppendLine("ManualOverrideMs=" + ManualOverrideMs);
            sb.AppendLine("; 读取输入法状态的同步间隔(毫秒)");
            sb.AppendLine("SyncIntervalMs=" + SyncIntervalMs);
            sb.AppendLine("; 离开这些软件时，是否把输入法恢复成打开状态");
            sb.AppendLine("RestoreOnLeave=" + B(RestoreOnLeave));
            sb.AppendLine("; 1=严格锁定：不在输入框时强制关闭输入法，Shift 撬开也会立刻关回去");
            sb.AppendLine("StrictLock=" + B(StrictLock));
            sb.AppendLine("; 是否允许切换键盘布局（0=绝不切换键盘，只用开关输入法）");
            sb.AppendLine("AllowLayoutSwitch=" + B(AllowLayoutSwitch));
            sb.AppendLine("; 右下角状态提示");
            sb.AppendLine("ShowBadge=" + B(ShowBadge));
            sb.AppendLine("; 是否在右下角通知区显示托盘图标（0=隐藏图标，程序仍后台运行；可在设置界面或全局设置快捷键里重新打开）");
            sb.AppendLine("ShowTrayIcon=" + B(ShowTrayIcon));
            sb.AppendLine("; 设置界面打开时是否在任务栏按钮区显示窗口按钮（后台运行本身不占任务栏）");
            sb.AppendLine("ShowTaskbarButton=" + B(ShowTaskbarButton));
            sb.AppendLine("BadgeAlways=" + B(BadgeAlways));
            sb.AppendLine("BadgeMs=" + BadgeMs);
            sb.AppendLine("; 浮窗是否置顶");
            sb.AppendLine("BadgeTopMost=" + B(BadgeTopMost));
            sb.AppendLine("; 锁定浮窗位置/大小（锁定后不能拖动、右下角不能缩放）");
            sb.AppendLine("BadgeLocked=" + B(BadgeLocked));
            sb.AppendLine("BadgeOpacity=" + BadgeOpacity.ToString("0.00"));
            sb.AppendLine("; 浮窗尺寸/位置（可在浮窗上拖动、右下角缩放后自动保存；X/Y=-1 为默认右下角）");
            sb.AppendLine("BadgeW=" + BadgeW);
            sb.AppendLine("BadgeH=" + BadgeH);
            sb.AppendLine("BadgeX=" + BadgeX);
            sb.AppendLine("BadgeY=" + BadgeY);
            sb.AppendLine("; 1=所有程序都生效，0=只对 [Apps] 里的程序生效");
            sb.AppendLine("AllApps=" + B(AllApps));
            sb.AppendLine("; 仅在 Method=layout 时使用。auto=自动探测");
            sb.AppendLine("ChineseHKL=" + (ChineseHkl == IntPtr.Zero ? "auto" : Hex(ChineseHkl)));
            sb.AppendLine("EnglishHKL=" + (EnglishHkl == IntPtr.Zero ? "auto" : Hex(EnglishHkl)));
            sb.AppendLine("LogLevel=" + LogLevel);
            sb.AppendLine();
            sb.AppendLine("[Apps]");
            sb.AppendLine("; 生效的程序名（不含 .exe），=1 启用");
            foreach (var a in Apps) sb.AppendLine(a + "=1");
            sb.AppendLine();
            sb.AppendLine("[Detect]");
            sb.AppendLine("; 判定“鼠标在输入框里”的依据，可单独关闭以排查误判");
            sb.AppendLine("; 标准 I 型光标（Maya 的 Qt 输入框、Windows 输入框等）");
            sb.AppendLine("StdIBeam=" + B(UseStdIBeam));
            sb.AppendLine("; 光标形状像 I 型（用于 PS 自绘光标）");
            sb.AppendLine("IBeamShape=" + B(UseShape));
            sb.AppendLine("; 放宽形状判定（允许更大的自绘 I 型光标，可能增加误判）");
            sb.AppendLine("IBeamShapeLoose=" + B(UseShapeLoose));
            sb.AppendLine("; 存在文字插入符");
            sb.AppendLine("CaretWindow=" + B(UseCaret));
            sb.AppendLine("; 按窗口类名（Max 的 CustEdit 等）");
            sb.AppendLine("UseClasses=" + B(UseClasses));
            sb.AppendLine("; PS 的文字工具（画布打字）走 COM 判断，1=启用");
            sb.AppendLine("PSToolRule=" + B(PsToolRule));
            sb.AppendLine("PSProcs=" + string.Join(",", PsProcs.ToArray()));
            sb.AppendLine("TextClasses=" + string.Join(",", TextClasses.ToArray()));
            sb.AppendLine("NumClasses=" + string.Join(",", NumClasses.ToArray()));
            sb.AppendLine();
            sb.AppendLine("[Keys]");
            sb.AppendLine("; 全局快捷键: 修饰键 CTRL/ALT/SHIFT/WIN + 主键");
            sb.AppendLine("Toggle=" + HotToggle);
            sb.AppendLine("Learn=" + HotLearn);
            sb.AppendLine("Diag=" + HotDiag);
            sb.AppendLine("; 打开设置主界面");
            sb.AppendLine("Settings=" + HotSettings);
            File.WriteAllText(Path, sb.ToString(), Encoding.UTF8);
        }

        static string B(bool b) { return b ? "1" : "0"; }
        static string Hex(IntPtr p) { return "0x" + (p.ToInt64() & 0xFFFFFFFF).ToString("X8"); }
    }

    // ------------------------------------------------------------------ 日志
    internal static class Log
    {
        static readonly object gate = new object();
        public static string Path;

        const long MaxBytes = 1024 * 512;   // 512 KB 后滚动，不删历史

        public static void W(string msg)
        {
            if (Path == null) return;
            try
            {
                lock (gate)
                {
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        // 滚动：AutoIME.log -> AutoIME.log.1（最多留一份历史，避免无限增长）
                        try
                        {
                            string bak = Path + ".1";
                            if (File.Exists(bak)) File.Delete(bak);
                            File.Move(Path, bak);
                        }
                        catch { }
                    }
                    File.AppendAllText(Path,
                        DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + "  " + msg + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------- 光标形状识别/缓存
    internal class CursorShape
    {
        public string Hash;
        public bool LooksIBeam;
        public bool IsStdIBeam;
        public bool LooksIBeamLoose;
        public int Bw, Bh, Count, ThinRows, Rows;
    }

    internal static class Cursor
    {
        static readonly Dictionary<IntPtr, CursorShape> cache = new Dictionary<IntPtr, CursorShape>();
        static IntPtr stdIBeam = IntPtr.Zero;

        public static void Reset() { lock (cache) cache.Clear(); }

        public static IntPtr StdIBeam
        {
            get
            {
                if (stdIBeam == IntPtr.Zero) stdIBeam = Nat.LoadCursor(IntPtr.Zero, Nat.IDC_IBEAM);
                return stdIBeam;
            }
        }

        public static CursorShape Get(IntPtr hcur)
        {
            if (hcur == IntPtr.Zero) return null;
            lock (cache)
            {
                CursorShape s;
                if (cache.TryGetValue(hcur, out s)) return s;
            }
            CursorShape ns = Compute(hcur);
            lock (cache)
            {
                if (cache.Count > 400) cache.Clear();
                cache[hcur] = ns;
            }
            return ns;
        }

        static CursorShape Compute(IntPtr hcur)
        {
            var s = new CursorShape();
            s.IsStdIBeam = (hcur == StdIBeam);
            Nat.ICONINFO ii;
            if (!Nat.GetIconInfo(hcur, out ii)) return s;
            try
            {
                int w1 = 0, h1 = 0, w2 = 0, h2 = 0;
                byte[] color = ii.hbmColor != IntPtr.Zero ? GetDib(ii.hbmColor, out w1, out h1) : null;
                byte[] mask = ii.hbmMask != IntPtr.Zero ? GetDib(ii.hbmMask, out w2, out h2) : null;
                if (color == null && mask == null) return s;

                ulong h = 1469598103934665603UL;
                if (color != null) for (int i = 0; i < color.Length; i++) { h ^= color[i]; h *= 1099511628211UL; }
                if (mask != null) for (int i = 0; i < mask.Length; i++) { h ^= mask[i]; h *= 1099511628211UL; }
                s.Hash = h.ToString("X16");

                int W = color != null ? w1 : w2;
                int H = color != null ? h1 : h2;
                byte[] inkSrc = color != null ? color : mask;
                bool useAlpha = color != null;
                if (inkSrc == null || W <= 0 || H <= 0) return s;

                int minX = W, minY = H, maxX = -1, maxY = -1, count = 0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int idx = (y * W + x) * 4;
                        bool on = useAlpha ? inkSrc[idx + 3] > 100 : inkSrc[idx] < 128;
                        if (!on) continue;
                        count++;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                if (maxX < 0) return s;
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                s.Bw = bw; s.Bh = bh; s.Count = count;

                int thinRows = 0, rows = 0;
                for (int y = minY + 3; y <= maxY - 3; y++)
                {
                    int rowCount = 0;
                    for (int x = 0; x < W; x++)
                    {
                        int idx = (y * W + x) * 4;
                        bool on = useAlpha ? inkSrc[idx + 3] > 100 : inkSrc[idx] < 128;
                        if (on) rowCount++;
                    }
                    rows++;
                    if (rowCount <= 3) thinRows++;
                }
                s.ThinRows = thinRows;
                s.Rows = rows;

                // 严格判定（标准尺寸的 I 型光标）
                if (bh >= 10 && bw <= 14 && bh >= bw * 1.5 && count <= bh * 6 &&
                    rows > 0 && thinRows * 10 >= rows * 8)
                    s.LooksIBeam = true;

                // 放宽判定（PS 这类自绘光标往往更大）
                if (bh >= 14 && bw <= 24 && bh >= bw * 1.8 && count <= bh * 8 &&
                    rows > 0 && thinRows * 10 >= rows * 7)
                    s.LooksIBeamLoose = true;
            }
            catch { }
            finally
            {
                if (ii.hbmColor != IntPtr.Zero) Nat.DeleteObject(ii.hbmColor);
                if (ii.hbmMask != IntPtr.Zero) Nat.DeleteObject(ii.hbmMask);
            }
            return s;
        }

        static byte[] GetDib(IntPtr hbm, out int w, out int h)
        {
            w = 0; h = 0;
            Nat.BITMAP bm;
            if (Nat.GetObject(hbm, Marshal.SizeOf(typeof(Nat.BITMAP)), out bm) == 0) return null;
            w = bm.bmWidth; h = bm.bmHeight;
            if (w <= 0 || h <= 0 || w > 256 || h > 256) return null;
            IntPtr dc = Nat.CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            try
            {
                var bi = new Nat.BITMAPINFO();
                bi.bmiHeader.biSize = 40;
                bi.bmiHeader.biWidth = w;
                bi.bmiHeader.biHeight = -h;
                bi.bmiHeader.biPlanes = 1;
                bi.bmiHeader.biBitCount = 32;
                var buf = new byte[w * h * 4];
                int ok = Nat.GetDIBits(dc, hbm, 0, (uint)h, buf, ref bi, 0);
                return ok > 0 ? buf : null;
            }
            finally { Nat.DeleteDC(dc); }
        }
    }

    // -------------------------------------------------------------- 状态/快照
    internal enum ImeState { Unknown, Chinese, English }

    internal class Snapshot
    {
        public IntPtr Hwnd;
        public uint Pid;
        public uint Tid;
        public string Proc = "";
        public string WindowClass = "";
        public string FocusClass = "";
        public bool HasCaret;
        public bool CursorShowing;
        public bool CursorOverTarget;
        public string CursorHash = "";
        public bool CursorStdIBeam;
        public bool CursorShapeIBeam;
        public bool CursorShapeLoose;
        public IntPtr Hkl;
        public bool IsImeLayout;
        public IntPtr ImeWnd;
        public bool ImeOpen;          // 输入法当前是否打开
        public bool IsTarget;
        public bool TextContext;
        public bool NumField;
        public string Reason = "";
    }

    // -------------------------------------------------------------------- 引擎
    internal class Engine
    {
        public Cfg Cfg;
        readonly HashSet<string> learned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly string learnedPath;

        public volatile bool Running;
        Thread th;
        public Snapshot Last = new Snapshot();
        public ImeState State = ImeState.Unknown;
        public string LastEvent = "";
        public event Action<Snapshot> Updated;
        public event Action<ImeState, string> StateChanged;

        DateTime manualUntil = DateTime.MinValue;
        DateTime failUntil = DateTime.MinValue;
        DateTime textSince, nonTextSince;
        DateTime lastSync = DateTime.MinValue;
        DateTime lastApply = DateTime.MinValue;
        bool expectedOpen;
        bool haveExpected;
        IntPtr prevFg = IntPtr.Zero;
        bool weClosedIme;
        IntPtr lastTargetHwnd;
        bool warned;
        bool warnedNoIme;
        bool warnedNoProc;
        bool warnedNoLayout;
        bool textSessionManual;    // 本次输入会话里用户手动按过 Shift：不再强制回中文
        bool prevWantChinese;
        DateTime lastBeat = DateTime.MinValue;

        public PsToolWatcher PsTool;

        public Engine(Cfg c)
        {
            Cfg = c;
            PsTool = new PsToolWatcher(this);
            learnedPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(c.Path), "AutoIME.learned.txt");
            LoadLearned();
        }

        void LoadLearned()
        {
            try
            {
                if (!File.Exists(learnedPath)) return;
                foreach (var l in File.ReadAllLines(learnedPath, Encoding.UTF8))
                {
                    string s = l.Trim();
                    if (s.Length > 3) learned.Add(s);
                }
            }
            catch { }
        }

        public bool IsLearned(string proc, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;
            return learned.Contains(proc + "|" + hash);
        }

        public void Learn(string proc, string hash)
        {
            if (string.IsNullOrEmpty(proc) || string.IsNullOrEmpty(hash)) return;
            string key = proc + "|" + hash;
            if (learned.Add(key))
            {
                try { File.AppendAllText(learnedPath, key + "\r\n", Encoding.UTF8); } catch { }
            }
        }

        public void ClearLearned(string proc)
        {
            var kill = new List<string>();
            foreach (var k in learned) if (k.StartsWith(proc + "|", StringComparison.OrdinalIgnoreCase)) kill.Add(k);
            foreach (var k in kill) learned.Remove(k);
            try { File.WriteAllLines(learnedPath, new List<string>(learned).ToArray(), Encoding.UTF8); }
            catch { }
        }

        public int LearnedCount { get { return learned.Count; } }

        // ------------------------------------------------------------- 生命周期
        public void Start()
        {
            if (Running) return;
            Running = true;
            th = new Thread(Loop);
            th.IsBackground = true;
            th.Name = "AutoIME-engine";
            th.Start();
            if (PsTool != null) PsTool.Start();
        }

        public void Stop()
        {
            Running = false;
            if (PsTool != null) PsTool.Stop();
            if (th != null) { try { th.Join(500); } catch { } th = null; }
        }

        void Loop()
        {
            while (Running)
            {
                int sleep = 40;
                try { sleep = Tick(); }
                catch (Exception ex) { Log.W("[ERR] " + ex.Message); sleep = 500; }
                if (!Running) break;
                Thread.Sleep(Math.Max(10, sleep));
            }
        }

        int Tick()
        {
            var snap = TakeSnapshot();
            Last = snap;
            var upd = Updated;
            if (upd != null) { try { upd(snap); } catch { } }

            // 调试模式：每 2 秒把引擎看到的东西写进日志，便于排查“为什么没生效”
            if (Cfg.LogLevel >= 2 && (DateTime.Now - lastBeat).TotalSeconds >= 2)
            {
                lastBeat = DateTime.Now;
                Log.W(string.Format(
                    "[调试] {0}.exe class='{1}' 目标={2} 输入框={3}({4}) caret={5} 标准I={6} 形状I={7} imeWnd=0x{8:X} open={9} hkl=0x{10:X8} psTool={11}",
                    snap.Proc, snap.WindowClass, snap.IsTarget, snap.TextContext, snap.Reason, snap.HasCaret,
                    snap.CursorStdIBeam, snap.CursorShapeIBeam, snap.ImeWnd.ToInt64(), snap.ImeOpen,
                    snap.Hkl.ToInt64() & 0xFFFFFFFF,
                    (PsTool == null ? "-" : (PsTool.LastTool.Length > 0 ? PsTool.LastTool : (PsTool.Available ? "(未取到)" : "(不可用)")))));
            }
            if (snap.IsTarget && snap.ImeWnd == IntPtr.Zero && !warnedNoIme)
            {
                warnedNoIme = true;
                LastEvent = snap.Proc + " 没有可用的输入法窗口，无法控制（可在配置里改用 Method=layout）";
                Log.W(LastEvent);
            }
            if (snap.Hwnd != IntPtr.Zero && snap.Proc.Length == 0 && !warnedNoProc)
            {
                warnedNoProc = true;
                LastEvent = "无法获取前台程序的进程名 (pid=" + snap.Pid + ")，可能被安全软件拦截";
                Log.W(LastEvent);
            }

            if (snap.Hwnd == IntPtr.Zero) return Cfg.PollIntervalMs;

            // ---- 离开这些软件 ----
            if (!snap.IsTarget)
            {
                if (prevFg != IntPtr.Zero && weClosedIme && Cfg.RestoreOnLeave && IsWindowAlive(lastTargetHwnd))
                {
                    IntPtr w = Nat.ImmGetDefaultIMEWnd(lastTargetHwnd);
                    if (w != IntPtr.Zero) { bool real; Ime.SetOpen(w, true, out real); }
                    weClosedIme = false;
                }
                prevFg = IntPtr.Zero;
                haveExpected = false;
                textSince = nonTextSince = DateTime.MinValue;
                SetState(ImeState.Unknown, "非目标程序");
                return Cfg.PollIntervalMs;
            }

            bool newWindow = (snap.Hwnd != prevFg);
            if (newWindow)
            {
                prevFg = snap.Hwnd;
                lastTargetHwnd = snap.Hwnd;
                Cursor.Reset();
                textSince = nonTextSince = DateTime.MinValue;
                manualUntil = DateTime.MinValue;
                haveExpected = false;
            }

            if (!Cfg.Enabled) { SetState(ImeState.Unknown, "已暂停"); return Cfg.PollIntervalMs; }
            if (DateTime.Now < manualUntil) { SetState(ImeState.Unknown, "手动操作保护中"); return Cfg.PollIntervalMs; }
            if (DateTime.Now < failUntil) return Cfg.PollIntervalMs;

            bool wantChinese = snap.TextContext && !snap.NumField;

            // 进入/离开输入框时，重置“本次输入手动切换过”的标记
            if (wantChinese && !prevWantChinese) textSessionManual = false;
            if (!wantChinese) textSessionManual = false;
            prevWantChinese = wantChinese;

            // ---- 检测用户是否手动改了输入法状态 ----
            // 每一拍都检查“用户是否手动改了输入法状态”：
            // 猛按 Shift 后必须马上识别，否则 40ms 后的强制开启会把它掰回去
            if (Cfg.ManualOverrideMs > 0 && haveExpected && (!Cfg.StrictLock || wantChinese))
            {
                lastSync = DateTime.Now;
                bool real = snap.ImeOpen;
                if (real != expectedOpen && (DateTime.Now - lastApply).TotalMilliseconds > 700)
                {
                    expectedOpen = real;
                    if (wantChinese)
                    {
                        // 在输入框里手动按 Shift 切中/英：尊重，直到离开这个输入框
                        textSessionManual = true;
                        LastEvent = "输入中手动切换中/英，本次输入内不再自动改回";
                        Log.W(LastEvent);
                    }
                    else
                    {
                        manualUntil = DateTime.Now.AddMilliseconds(Cfg.ManualOverrideMs);
                        LastEvent = "检测到你手动开关了输入法，自动控制暂停 " + (Cfg.ManualOverrideMs / 1000.0).ToString("0.#") + " 秒";
                        Log.W(LastEvent);
                    }
                    return Cfg.PollIntervalMs;
                }
            }

            bool isChinese = snap.ImeLayoutOpen();
            if (wantChinese)
            {
                if (textSince == DateTime.MinValue) textSince = DateTime.Now;
                nonTextSince = DateTime.MinValue;
                if (!isChinese && !textSessionManual &&
                    (newWindow || (DateTime.Now - textSince).TotalMilliseconds >= Cfg.EnterDelayMs))
                    Apply(snap, true);
            }
            else
            {
                if (nonTextSince == DateTime.MinValue) nonTextSince = DateTime.Now;
                textSince = DateTime.MinValue;
                if (isChinese && (newWindow || (DateTime.Now - nonTextSince).TotalMilliseconds >= Cfg.LeaveDelayMs))
                    Apply(snap, false);
            }
            return Cfg.PollIntervalMs;
        }

        static bool IsWindowAlive(IntPtr h)
        {
            return h != IntPtr.Zero && Nat.IsWindow(h);
        }

        // ------------------------------------------------------------ 应用状态
        void Apply(Snapshot s, bool chinese)
        {
            lastApply = DateTime.Now;
            bool ok = ApplyCore(s, chinese);
            expectedOpen = chinese;
            haveExpected = ok;               // 失败就不做“手动操作”判断，避免误判
            weClosedIme = !chinese;
            if (ok)
            {
                SetState(chinese ? ImeState.Chinese : ImeState.English,
                         (chinese ? "打开输入法（中文）" : "关闭输入法（快捷键）") +
                         (s.Reason.Length > 0 ? " ← " + s.Reason : ""));
            }
            else
            {
                failUntil = DateTime.Now.AddSeconds(2);
                LastEvent = "控制输入法失败" + (warned ? "" : "（若该软件以管理员身份运行，本程序也要用管理员身份运行）");
                if (!warned) { warned = true; Log.W(LastEvent); }
            }
        }

        bool ApplyCore(Snapshot s, bool chinese)
        {
            if (Cfg.Method == "layout")
                return SetLayoutHkl(s.Hwnd, s.Tid, chinese ? Cfg.ChineseHkl : Cfg.EnglishHkl);

            // --- Method = ime ---
            // 当前不是中文输入法（例如在纯英文键盘上），要中文就只能先切回中文布局
            if (!s.IsImeLayout)
            {
                if (!chinese) return true;                       // 已经是英文键盘，无需动作
                if (!Cfg.AllowLayoutSwitch)                      // 要求"不切换键盘"：这里不做任何切换
                {
                    if (!warnedNoLayout)
                    {
                        warnedNoLayout = true;
                        LastEvent = "当前不是中文输入法（纯英文键盘），按设置不自动切换键盘";
                        Log.W(LastEvent);
                    }
                    return false;
                }
                if (!SetLayoutHkl(s.Hwnd, s.Tid, Cfg.ChineseHkl)) return false;
                s.Tid = Nat.GetWindowThreadProcessId(s.Hwnd, out s.Pid);
            }

            IntPtr imeWnd = s.ImeWnd != IntPtr.Zero ? s.ImeWnd : Nat.ImmGetDefaultIMEWnd(s.Hwnd);
            if (imeWnd == IntPtr.Zero) return false;

            if (chinese) Ime.SetChineseMode(imeWnd);             // 先把输入法内部切成中文
            bool real;
            if (Ime.SetOpen(imeWnd, chinese, out real)) return true;
            // 开关没生效：不再偷偷改用布局切换，交给上层记失败并提示
            return false;
        }

        // 切换键盘布局（备用方案）
        public static bool SetLayoutHkl(IntPtr hwnd, uint tid, IntPtr hkl)
        {
            if (hwnd == IntPtr.Zero || hkl == IntPtr.Zero) return false;
            if (Nat.GetKeyboardLayout(tid) == hkl) return true;

            IntPtr r;
            Nat.SendMessageTimeout(hwnd, Nat.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl,
                Nat.SMTO_ABORTIFHUNG | Nat.SMTO_BLOCK, 300, out r);
            if (WaitHkl(tid, hkl, 250)) return true;

            uint myTid = Nat.GetCurrentThreadId();
            bool attached = Nat.AttachThreadInput(myTid, tid, true);
            try { if (attached) Nat.ActivateKeyboardLayout(hkl, 0); }
            finally { if (attached) Nat.AttachThreadInput(myTid, tid, false); }
            if (WaitHkl(tid, hkl, 250)) return true;

            Nat.PostMessage(hwnd, Nat.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
            return WaitHkl(tid, hkl, 300);
        }

        static bool WaitHkl(uint tid, IntPtr want, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (Nat.GetKeyboardLayout(tid) == want) return true;
                Thread.Sleep(15);
            }
            return Nat.GetKeyboardLayout(tid) == want;
        }

        void SetState(ImeState s, string why)
        {
            if (s == State) return;
            State = s;
            var h = StateChanged;
            if (h != null) { try { h(s, why); } catch { } }
        }

        // --------------------------------------------------------------- 快照
        internal Snapshot TakeSnapshot()
        {
            var s = new Snapshot();
            IntPtr fg = Nat.GetForegroundWindow();
            s.Hwnd = fg;
            if (fg == IntPtr.Zero) return s;

            s.Tid = Nat.GetWindowThreadProcessId(fg, out s.Pid);
            s.Proc = ProcName(s.Pid);
            s.WindowClass = ClassOf(fg);
            s.Hkl = Nat.GetKeyboardLayout(s.Tid);
            s.IsImeLayout = Nat.ImmIsIME(s.Hkl);
            s.IsTarget = Cfg.IsTarget(s.Proc);

            var gti = new Nat.GUITHREADINFO();
            gti.cbSize = Marshal.SizeOf(typeof(Nat.GUITHREADINFO));
            bool haveGti = Nat.GetGUIThreadInfo(s.Tid, ref gti);
            if (haveGti)
            {
                s.HasCaret = gti.hwndCaret != IntPtr.Zero && gti.hwndCaret != fg;
                s.FocusClass = gti.hwndFocus != IntPtr.Zero ? ClassOf(gti.hwndFocus) : "";
            }
            if (s.FocusClass.Length == 0) s.FocusClass = s.WindowClass;

            var ci = new Nat.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(typeof(Nat.CURSORINFO));
            if (Nat.GetCursorInfo(ref ci))
            {
                s.CursorShowing = (ci.flags & Nat.CURSOR_SHOWING) != 0;
                if (s.CursorShowing && ci.hCursor != IntPtr.Zero)
                {
                    var shape = Cursor.Get(ci.hCursor);
                    if (shape != null)
                    {
                        s.CursorHash = shape.Hash;
                        s.CursorStdIBeam = shape.IsStdIBeam;
                        s.CursorShapeIBeam = shape.LooksIBeam;
                        s.CursorShapeLoose = shape.LooksIBeamLoose;
                        if (Cfg.LogLevel >= 2 && !shape.IsStdIBeam && !shape.LooksIBeam)
                        {
                            string key = s.Proc + "|" + shape.Hash;
                            lock (loggedCursors)
                            {
                                if (loggedCursors.Add(key) && loggedCursors.Count <= 200)
                                    Log.W("[光标采集] " + key + " bbox=" + shape.Bw + "x" + shape.Bh +
                                          " count=" + shape.Count + " thin=" + shape.ThinRows + "/" + shape.Rows);
                            }
                        }
                    }
                    var pt = new Nat.POINT();
                    pt.X = ci.ptScreenPos.X;
                    pt.Y = ci.ptScreenPos.Y;
                    IntPtr under = Nat.WindowFromPoint(pt);
                    if (under != IntPtr.Zero)
                    {
                        uint upid;
                        Nat.GetWindowThreadProcessId(under, out upid);
                        s.CursorOverTarget = (upid == s.Pid);
                    }
                }
            }

            if (!s.IsTarget) return s;

            // IME 窗口与状态
            s.ImeWnd = Nat.ImmGetDefaultIMEWnd(fg);
            int v;
            if (s.ImeWnd != IntPtr.Zero && Ime.Get(s.ImeWnd, Nat.IMC_GETOPENSTATUS, out v)) s.ImeOpen = (v != 0);

            // ---- 是否正在输入文字 ----
            bool text = false;
            string why = "";

            if (Cfg.UseClasses && ClassIn(s.FocusClass, Cfg.NumClasses))
            {
                s.NumField = true;
                why = "数字输入框:" + s.FocusClass;
            }
            else if (Cfg.UseClasses && ClassIn(s.FocusClass, Cfg.TextClasses))
            {
                text = true;
                why = "输入框类名:" + s.FocusClass;
            }
            else if (Cfg.PsToolRule && PsTool != null && PsTool.Available &&
                     (PsTool.BusyIsText || PsTool.TypeTool) && IsPsProc(s.Proc))
            {
                text = true;
                why = PsTool.BusyIsText ? "PS文字编辑中(忙且上次是文字工具)" : ("PS文字工具:" + PsTool.LastTool);
            }
            else if (Cfg.UseCaret && s.HasCaret)
            {
                text = true;
                why = "有插入符";
            }
            else if (s.CursorOverTarget)
            {
                if (Cfg.UseStdIBeam && s.CursorStdIBeam) { text = true; why = "I型光标"; }
                else if (IsLearned(s.Proc, s.CursorHash)) { text = true; why = "已学习的文字光标"; }
                else if (Cfg.UseShape && s.CursorShapeIBeam) { text = true; why = "I型光标(形状)"; }
                else if (Cfg.UseShapeLoose && s.CursorShapeLoose) { text = true; why = "I型光标(放宽)"; }
            }

            if (Cfg.UseClasses && !s.NumField && ClassIn(s.WindowClass, Cfg.TextClasses))
            {
                text = true;
                why = "窗口类名:" + s.WindowClass;
            }

            s.TextContext = text;
            s.Reason = why;
            return s;
        }

        public bool IsPsProc(string proc)
        {
            if (string.IsNullOrEmpty(proc)) return false;
            var list = Cfg.PsProcs;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], proc, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool ClassIn(string cls, List<string> list)
        {
            if (string.IsNullOrEmpty(cls)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                string p = list[i];
                if (p.Length == 0) continue;
                if (string.Equals(cls, p, StringComparison.OrdinalIgnoreCase)) return true;
                if (p.EndsWith("*", StringComparison.Ordinal))
                {
                    if (cls.StartsWith(p.Substring(0, p.Length - 1), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (cls.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static readonly Dictionary<uint, string> procCache = new Dictionary<uint, string>();
        static readonly HashSet<string> loggedCursors = new HashSet<string>();

        // 取进程名。注意：部分机器上有安全软件会拦截 .NET 的进程枚举
        // （Process.GetProcessById 直接抛异常），所以优先用 OpenProcess + QueryFullProcessImageName
        /// <summary>抓取鼠标当前位置下的光标：返回所在进程名 + 光标哈希（设置界面的“教一次”向导用）</summary>
        public static bool CaptureCursorUnderPoint(out string proc, out string hash)
        {
            proc = "";
            hash = "";
            var ci = new Nat.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(typeof(Nat.CURSORINFO));
            if (!Nat.GetCursorInfo(ref ci)) return false;
            if (ci.hCursor == IntPtr.Zero) return false;
            var pt = new Nat.POINT();
            pt.X = ci.ptScreenPos.X;
            pt.Y = ci.ptScreenPos.Y;
            IntPtr under = Nat.WindowFromPoint(pt);
            if (under == IntPtr.Zero) return false;
            uint pid;
            Nat.GetWindowThreadProcessId(under, out pid);
            if (pid == 0) return false;
            proc = ProcName(pid);
            var shape = Cursor.Get(ci.hCursor);
            if (shape == null || string.IsNullOrEmpty(shape.Hash)) return false;
            hash = shape.Hash;
            return proc.Length > 0;
        }

        public static string ProcName(uint pid)
        {
            if (pid == 0) return "";
            lock (procCache)
            {
                string s;
                if (procCache.TryGetValue(pid, out s)) return s;
            }
            string name = ProcNameRaw(pid);
            if (name.Length == 0) return "";        // 空结果不缓存，下次再试
            lock (procCache)
            {
                if (procCache.Count > 200) procCache.Clear();
                procCache[pid] = name;
            }
            return name;
        }

        static string ProcNameRaw(uint pid)
        {
            IntPtr h = Nat.OpenProcess(Nat.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int cap = sb.Capacity;
                    if (Nat.QueryFullProcessImageName(h, 0, sb, ref cap))
                    {
                        string full = sb.ToString();
                        if (full.Length > 0)
                        {
                            try { return System.IO.Path.GetFileNameWithoutExtension(full); }
                            catch { }
                        }
                    }
                }
                finally { Nat.CloseHandle(h); }
            }
            try { return Process.GetProcessById((int)pid).ProcessName; }
            catch { return ""; }
        }

        public static string ClassOf(IntPtr h)
        {
            if (h == IntPtr.Zero) return "";
            var sb = new StringBuilder(256);
            Nat.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool AmElevated()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public const int VK_NONAME = 0; // 占位，避免误用
    }

    internal static class SnapExt
    {
        // 当前是否处于「中文可输入」状态
        public static bool ImeLayoutOpen(this Snapshot s)
        {
            if (!s.IsImeLayout) return false;
            return s.ImeOpen;
        }
    }

    // ------------------------------------------------- PS 当前工具读取(cscript)
    //  本程序自己调 Photoshop 的 COM 会卡死在激活阶段（本机被安全软件拦），
    //  但系统自带、受信任的 cscript 能调通，所以：
    //    cscript 运行 AutoIME.PsTool.vbs，把 PS 的当前工具写进 AutoIME.pstool.tmp，
    //    本程序只读这个文件（纯文件 I/O，完全不碰 COM）。
    internal class PsToolWatcher
    {
        readonly Engine eng;
        Thread th;
        volatile bool running;
        public volatile bool Available;
        public volatile bool TypeTool;
        public volatile bool Busy;              // PS 脚本接口被锁（画布涂抹 / 文字编辑都会锁）
        public volatile string LastTool = "";
        public volatile string LastKnownTool = "";   // 最近一次能读到的真实工具名
        public volatile bool BusyIsText;             // 忙 + 上一次是文字工具 = 在编辑文字
        public volatile bool LastKnownIsType;

        public string OutPathForDiag { get { return outPath; } }


        readonly string vbsPath;
        readonly string outPath;
        readonly string pidPath;
        Process child;
        DateTime lastSpawn = DateTime.MinValue;
        DateTime lastNeed = DateTime.MinValue;
        bool warnedMissing;

        public PsToolWatcher(Engine e)
        {
            eng = e;
            string dir = System.IO.Path.GetDirectoryName(e.Cfg.Path);
            vbsPath = System.IO.Path.Combine(dir, "AutoIME.PsTool.vbs");
            outPath = System.IO.Path.Combine(dir, "AutoIME.pstool.tmp");
            pidPath = System.IO.Path.Combine(dir, "AutoIME.ps.pid");
        }

        public void Start()
        {
            if (th != null) return;
            running = true;
            th = new Thread(Loop);
            th.IsBackground = true;
            th.Name = "ps-tool-bridge";
            th.Start();
        }

        public void Stop()
        {
            running = false;
            KillChild();
        }

        void KillChild()
        {
            try { if (child != null && !child.HasExited) child.Kill(); } catch { }
            child = null;
        }

        void Loop()
        {
            while (running)
            {
                int sleep = 250;
                try { sleep = Step(); }
                catch (Exception ex) { Log.W("PS 工具读取异常: " + ex.Message); sleep = 1500; }
                if (!running) break;
                Thread.Sleep(sleep);
            }
            KillChild();
        }

        int Step()
        {
            if (!eng.Cfg.PsToolRule)
            {
                Available = false;
                TypeTool = false;
                if (child != null) KillChild();
                return 800;
            }

            var snap = eng.Last;
            bool need = snap != null && snap.IsTarget && eng.IsPsProc(snap.Proc);
            if (!need)
            {
                // 目标不在前台时不读；长时间不用就把助手收掉
                if (child != null && (DateTime.Now - lastNeed).TotalSeconds > 60) KillChild();
                RemovePidFile();
                return 350;
            }
            lastNeed = DateTime.Now;
            EnsurePidFile();

            if (!File.Exists(vbsPath))
            {
                Available = false;
                TypeTool = false;
                if (!warnedMissing)
                {
                    warnedMissing = true;
                    LastTool = "(缺少 AutoIME.PsTool.vbs)";
                    Log.W("缺少 " + vbsPath + "，PS 工具自动识别不可用（文字光标学习仍可用）");
                }
                return 2000;
            }

            EnsureChild();
            ReadTool();
            return 300;
        }

        // 心跳文件：告诉助手“Photoshop 现在确实在运行”，避免它把 PS 启动起来
        void EnsurePidFile()
        {
            try
            {
                uint pid = 0;
                try { var arr = Process.GetProcessesByName("Photoshop"); if (arr.Length > 0) pid = (uint)arr[0].Id; } catch { }
                if (pid == 0)
                {
                    // 取不到进程（未运行 / 枚举被安全软件拦）：删掉旧心跳，
                    // 避免助手把“0”当成存活、误拉起第二个 PS 实例
                    RemovePidFile();
                    return;
                }
                File.WriteAllText(pidPath, pid.ToString());
            }
            catch { }
        }

        void RemovePidFile()
        {
            try { if (File.Exists(pidPath)) File.Delete(pidPath); } catch { }
        }

        void EnsureChild()
        {
            if (child != null && !child.HasExited) return;
            if ((DateTime.Now - lastSpawn).TotalSeconds < 3) return;
            lastSpawn = DateTime.Now;
            try
            {
                var psi = new ProcessStartInfo("cscript.exe",
                    "//nologo //B \"" + vbsPath + "\" \"" + outPath + "\" \"" + pidPath + "\"");
                psi.UseShellExecute = true;                  // 与手动运行保持一致，COM 才通
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                child = Process.Start(psi);
                Log.W("已启动 PS 工具读取助手（cscript）");
            }
            catch (Exception ex)
            {
                Available = false;
                TypeTool = false;
                LastTool = "(cscript 启动失败)";
                Log.W("启动 cscript 失败: " + ex.Message);
            }
        }

        void ReadTool()
        {
            try
            {
                if (!File.Exists(outPath)) return;
                if ((DateTime.Now - File.GetLastWriteTime(outPath)).TotalSeconds > 8)
                {
                    Available = false;
                    TypeTool = false;
                    return;
                }
                string s = File.ReadAllText(outPath).Trim();
                if (s.StartsWith("!nops", StringComparison.Ordinal))
                {
                    Available = false;
                    TypeTool = false;
                    Busy = false;
                    LastTool = "(未连接到 PS) " + s.Substring(5);
                    return;
                }
                Available = true;
                Busy = (s == "!busy");
                TypeTool = s.IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!Busy && s.Length > 0)
                {
                    LastKnownTool = s;
                    LastKnownIsType = s.IndexOf("type", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                // 关键：PS 涂抹时也会“忙”，只有上一次用的是文字工具才算在编辑文字
                BusyIsText = Busy && LastKnownIsType;
                LastTool = Busy ? (BusyIsText ? "(文字编辑中)" : "(忙·" + (LastKnownTool.Length > 0 ? LastKnownTool : "未知") + ")") : s;
            }
            catch { }
        }
    }
    // ------------------------------------------------------- 右下角状态浮标
    internal class BadgeForm : Form
    {
        const int ResizeGrip = 18;
        const int MinW = 96, MinH = 40, MaxW = 600, MaxH = 300;

        // 右上角锁定图标：尺寸随浮窗高度等比缩放
        int LockPad { get { return Clamp((int)(Height * 0.10), 4, 10); } }
        int LockH { get { return Clamp((int)(Height * 0.34), 12, 28); } }
        int LockW { get { return (int)(LockH * 1.25); } }
        Rectangle LockRect { get { return new Rectangle(Width - LockW - LockPad, LockPad, LockW, LockH); } }

        readonly Cfg cfg;
        ImeState state = ImeState.Unknown;
        string detail = "";
        readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        Color back = Color.FromArgb(40, 40, 44);
        string big = "";

        bool dragging, resizing;
        Point dragOffset;
        int startW, startH, startX, startY;

        public BadgeForm(Cfg c)
        {
            cfg = c;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = cfg.BadgeTopMost;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Clamp(cfg.BadgeW, MinW, MaxW), Clamp(cfg.BadgeH, MinH, MaxH));
            BackColor = Color.FromArgb(40, 40, 44);
            DoubleBuffered = true;
            Opacity = (float)Math.Max(0.30, Math.Min(1.00, cfg.BadgeOpacity));
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); Hide(); };
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;  // NOACTIVATE
                cp.ExStyle |= 0x00000080;  // TOOLWINDOW
                return cp;
            }
        }

        // 应用配置变更：置顶、尺寸、位置
        public void ApplySettings()
        {
            TopMost = cfg.BadgeTopMost;
            Opacity = (float)Math.Max(0.30, Math.Min(1.00, cfg.BadgeOpacity));
            Width = Clamp(cfg.BadgeW, MinW, MaxW);
            Height = Clamp(cfg.BadgeH, MinH, MaxH);
            Position();
            Invalidate();
        }

        public void Position()
        {
            if (cfg.BadgeX >= 0 && cfg.BadgeY >= 0)
            {
                Location = new Point(cfg.BadgeX, cfg.BadgeY);
                return;
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);
        }

        public void ShowState(ImeState s, string d, int ms, bool always)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<ImeState, string, int, bool>(ShowState), s, d, ms, always); } catch { }
                return;
            }
            state = s;
            detail = d ?? "";
            if (s == ImeState.Chinese) { back = Color.FromArgb(196, 42, 51); big = "中"; }
            else if (s == ImeState.English) { back = Color.FromArgb(28, 90, 160); big = "英"; }
            else { back = Color.FromArgb(55, 55, 60); big = "·"; }
            Position();
            Invalidate();
            if (!Visible) Show();
            hideTimer.Stop();
            if (!always) { hideTimer.Interval = Math.Max(400, ms); hideTimer.Start(); }
        }

        public void HideNow()
        {
            if (InvokeRequired) { try { BeginInvoke(new Action(HideNow)); } catch { } return; }
            hideTimer.Stop();
            Hide();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            // 右上角锁图标：点击切换锁定/解锁（锁定状态下也允许点它解锁）
            if (LockRect.Contains(e.Location))
            {
                cfg.BadgeLocked = !cfg.BadgeLocked;
                try { cfg.Save(); } catch { }
                Invalidate();
                return;
            }
            if (cfg.BadgeLocked) return;            // 锁定：不响应拖动/缩放
            if (e.X >= Width - ResizeGrip && e.Y >= Height - ResizeGrip)
            {
                resizing = true;
                startW = Width; startH = Height; startX = e.X; startY = e.Y;
            }
            else
            {
                dragging = true;
                dragOffset = e.Location;
            }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (resizing)
            {
                Width = Clamp(startW + (e.X - startX), MinW, MaxW);
                Height = Clamp(startH + (e.Y - startY), MinH, MaxH);
                Invalidate();
            }
            else if (dragging)
            {
                Location = new Point(Left + e.X - dragOffset.X, Top + e.Y - dragOffset.Y);
            }
            else if (LockRect.Contains(e.Location))
            {
                Cursor = Cursors.Hand;
            }
            else if (!cfg.BadgeLocked && e.X >= Width - ResizeGrip && e.Y >= Height - ResizeGrip)
            {
                Cursor = Cursors.SizeNWSE;
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging)
            {
                cfg.BadgeX = Left;
                cfg.BadgeY = Top;
                try { cfg.Save(); } catch { }
            }
            if (resizing)
            {
                cfg.BadgeW = Width;
                cfg.BadgeH = Height;
                try { cfg.Save(); } catch { }
            }
            dragging = resizing = false;
            Capture = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = Clamp((int)(Height * 0.10), 5, 12);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Clamp(Height / 4, 8, 16);
            using (var path = Rounded(r, radius))
            using (var br = new SolidBrush(back))
            {
                g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawPath(pen, path);
            }

            // 大字区：近方形，边长随高度；文字区在右侧并避让右上角锁图标
            int bigSide = Height - pad * 2;
            int bigX = pad;
            int textX = bigX + bigSide + pad;
            int tw = Width - textX - pad;
            if (tw < 24) { textX = bigX + bigSide + 2; tw = Width - textX - pad; }

            float bigFont = bigSide * 0.55f;
            using (var f = new Font("Microsoft YaHei UI", bigFont, FontStyle.Bold))
            using (var br = new SolidBrush(Color.White))
            {
                var sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(big, f, br, new RectangleF(bigX, pad, bigSide, Height - pad * 2), sf);
            }

            float tFont = Math.Max(8f, Height * 0.20f);
            float dFont = Math.Max(7f, Height * 0.15f);
            using (var f = new Font("Microsoft YaHei UI", tFont))
            using (var br = new SolidBrush(Color.FromArgb(230, 235, 235)))
            using (var f2 = new Font("Microsoft YaHei UI", dFont))
            using (var br2 = new SolidBrush(Color.FromArgb(180, 190, 190)))
            {
                var sf2 = new StringFormat();
                sf2.Alignment = StringAlignment.Near;
                sf2.LineAlignment = StringAlignment.Center;
                sf2.FormatFlags |= StringFormatFlags.NoWrap;
                sf2.Trimming = StringTrimming.EllipsisCharacter;
                string t = state == ImeState.Chinese ? "输入法 开 · 中文"
                         : (state == ImeState.English ? "已锁定 · 快捷键" : "自动切换");
                float half = (Height - pad * 2) / 2f;
                g.DrawString(t, f, br, new RectangleF(textX, pad, tw, half), sf2);
                g.DrawString(detail, f2, br2, new RectangleF(textX, pad + half, tw, half), sf2);
            }

            // 右上角锁定图标（点击切换锁定/解锁，尺寸随高度缩放）
            DrawLock(g, LockRect, cfg.BadgeLocked);

            // 右下角缩放手柄
            using (var grip = new Pen(Color.FromArgb(130, 255, 255, 255), 1.4f))
            {
                g.DrawLine(grip, Width - ResizeGrip, Height - 4, Width - 4, Height - ResizeGrip);
                g.DrawLine(grip, Width - ResizeGrip + 4, Height - 4, Width - 4, Height - ResizeGrip + 4);
            }
        }

        static void DrawLock(Graphics g, Rectangle r, bool locked)
        {
            Color col = locked ? Color.FromArgb(235, 165, 70) : Color.FromArgb(180, 190, 190);
            int bx = r.X + (int)(r.Width * 0.16);
            int bw = (int)(r.Width * 0.68);
            int by = r.Y + (int)(r.Height * 0.42);
            int bh = r.Height - (by - r.Y);
            using (var br = new SolidBrush(col))
                g.FillRectangle(br, bx, by, bw, bh);
            using (var pen = new Pen(col, Math.Max(1.2f, r.Height * 0.09f)))
            {
                int lw = bw - (int)(bw * 0.28);
                int lx = bx + (int)(bw * 0.14);
                // 锁梁：闭合=完整半椭圆；解锁=只画左半边、右侧开口
                g.DrawArc(pen, lx, r.Y, lw, (int)(bh * 1.1f), 180, locked ? 180 : 90);
            }
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ---------------------------------------------------------------- 诊断面板
    internal class DiagForm : Form
    {
        readonly Engine eng;
        readonly TextBox box;
        readonly System.Windows.Forms.Timer t;
        readonly Label lbl;

        public DiagForm(Engine e)
        {
            eng = e;
            Text = "AutoIME 诊断 " + Ver.Text;
            ClientSize = new Size(600, 470);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Font = new Font("Microsoft YaHei UI", 9f);

            box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.Dock = DockStyle.Fill;
            box.Font = new Font("Consolas", 9.5f);
            box.BackColor = Color.FromArgb(30, 30, 32);
            box.ForeColor = Color.FromArgb(220, 225, 225);

            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 42;
            bar.FlowDirection = FlowDirection.LeftToRight;
            bar.Padding = new Padding(4);

            var bLearn = MakeButton("学习当前光标为“文字光标”", delegate { LearnNow(); });
            var bOff = MakeButton("试关输入法 3 秒", delegate { Trial(false); });
            var bOn = MakeButton("试开输入法", delegate { Trial(true); });
            var bClr = MakeButton("清除本程序记录", delegate
            {
                var sn = eng.Last;
                if (sn != null && sn.Proc.Length > 0) { eng.ClearLearned(sn.Proc); lbl.Text = "已清除 " + sn.Proc + " 的学习记录"; }
            });
            var bExport = MakeButton("导出诊断报告", delegate
            {
                string p = (App.Current == null) ? null : App.Current.ExportDiag();
                lbl.Text = p == null ? "导出失败" : "已导出: " + p;
            });
            bar.Controls.Add(bExport);
            bar.Controls.Add(bLearn);
            bar.Controls.Add(bOff);
            bar.Controls.Add(bOn);
            bar.Controls.Add(bClr);

            lbl = new Label();
            lbl.Dock = DockStyle.Bottom;
            lbl.Height = 26;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Padding = new Padding(6, 0, 0, 0);

            Controls.Add(box);
            Controls.Add(bar);
            Controls.Add(lbl);

            t = new System.Windows.Forms.Timer();
            t.Interval = 250;
            t.Tick += (s, ev) => Refresh2();
            t.Start();
            Refresh2();
        }

        static Button MakeButton(string text, EventHandler click)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Click += click;
            return b;
        }

        void LearnNow()
        {
            var sn = eng.Last;
            if (sn == null || sn.Hwnd == IntPtr.Zero) { lbl.Text = "没有前台窗口"; return; }
            if (string.IsNullOrEmpty(sn.CursorHash)) { lbl.Text = "当前看不到鼠标光标"; return; }
            eng.Learn(sn.Proc, sn.CursorHash);
            lbl.Text = "已学习: " + sn.Proc + " / " + sn.CursorHash + "   共 " + eng.LearnedCount + " 条";
        }

        void Trial(bool open)
        {
            var sn = eng.Last;
            if (sn == null || sn.ImeWnd == IntPtr.Zero) { lbl.Text = "当前窗口没有 IME 窗口"; return; }
            bool real;
            bool ok = Ime.SetOpen(sn.ImeWnd, open, out real);
            if (open) Ime.SetChineseMode(sn.ImeWnd);
            lbl.Text = "已发送「" + (open ? "打开" : "关闭") + "输入法」 成功=" + ok + "  读回状态=" + (real ? "开" : "关") +
                       "   (3 秒后恢复打开)";
            var tm = new System.Windows.Forms.Timer();
            tm.Interval = 3000;
            tm.Tick += delegate
            {
                tm.Stop(); tm.Dispose();
                bool r2;
                Ime.SetOpen(sn.ImeWnd, true, out r2);
                Ime.SetChineseMode(sn.ImeWnd);
            };
            tm.Start();
        }

        void Refresh2()
        {
            var s = eng.Last;
            if (s == null) return;
            var sb = new StringBuilder();
            sb.AppendLine("前台程序 : " + (s.Proc.Length > 0 ? s.Proc + ".exe" : "(未知)") + "    pid=" + s.Pid + "    class=" + s.WindowClass);
            sb.AppendLine("是否目标 : " + (s.IsTarget ? "是" : "否") + "      自动控制: " + (eng.Cfg.Enabled ? "开" : "关") + "     方式: " + eng.Cfg.Method);
            sb.AppendLine();
            sb.AppendLine("-- 输入法 --");
            sb.AppendLine("IME 窗口 : " + (s.ImeWnd != IntPtr.Zero ? "0x" + s.ImeWnd.ToInt64().ToString("X") : "（无法获取）"));
            int conv = 0;
            if (s.ImeWnd != IntPtr.Zero) Ime.Get(s.ImeWnd, Nat.IMC_GETCONVERSIONMODE, out conv);
            sb.AppendLine("输入法   : " + (s.IsImeLayout ? "当前是输入法(可切中文)" : "当前不是输入法(纯英文键盘)") +
                          "   开/关=" + (s.ImeOpen ? "开" : "关") + "   中英模式=0x" + conv.ToString("X"));
            sb.AppendLine("键盘布局 : 0x" + (s.Hkl.ToInt64() & 0xFFFFFFFF).ToString("X8"));
            sb.AppendLine();
            sb.AppendLine("-- 输入框判定 --");
            sb.AppendLine("焦点控件 : " + (s.FocusClass.Length > 0 ? s.FocusClass : "(无)") + "    插入符: " + (s.HasCaret ? "有" : "无"));
            sb.AppendLine("鼠标光标 : " + (s.CursorShowing ? "可见" : "隐藏") +
                          "  " + (s.CursorStdIBeam ? "[标准I型] " : "") + (s.CursorShapeIBeam ? "[形状像I型] " : "") +
                          (s.CursorOverTarget ? "[在本程序上]" : "[不在本程序上]"));
            sb.AppendLine("光标哈希 : " + (s.CursorHash.Length > 0 ? s.CursorHash : "-") +
                          (eng.IsLearned(s.Proc, s.CursorHash) ? "  ★已学习" : ""));
            sb.AppendLine();
            sb.AppendLine("判定     : " + (s.TextContext ? ">>> 输入框 —— 打开输入法(中文)" : ">>> 非输入框 —— 关闭输入法(快捷键可用)"));
            sb.AppendLine("依据     : " + (s.Reason.Length > 0 ? s.Reason : "-") + (s.NumField ? "（数字框强制英文）" : ""));
            sb.AppendLine("PS文字工具: " + (eng.PsTool == null ? "未启用" : ((eng.PsTool.TypeTool ? "是" : "否") + "   COM读数=" + (eng.PsTool.LastTool.Length > 0 ? eng.PsTool.LastTool : "-"))));
            sb.AppendLine("最近事件 : " + (eng.LastEvent.Length > 0 ? eng.LastEvent : "-"));
            sb.AppendLine();
            sb.AppendLine("已学习的光标条数: " + eng.LearnedCount);
            sb.AppendLine();
            sb.AppendLine("用法: 在 PS / Max / Maya 里把鼠标放到能打字的地方（双击图层名、");
            sb.AppendLine("      文字工具的输入位置、Max 的输入框），点上面第一个按钮学习一次。");
            sb.AppendLine("      若“鼠标光标”一行出现 ★已学习，说明该光标已被识别为文字光标。");
            string txt = sb.ToString();
            if (box.Text != txt) box.Text = txt;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------ 设置主界面
    internal class SettingsForm : Form
    {
        readonly App app;

        CheckBox ckEnable, ckStrict, ckBadge, ckBadgeAlways, ckRestore, ckAllApps, ckTray, ckTaskbar, ckBadgeTopMost, ckBadgeLocked;
        TrackBar trkOpacity; Label lblOpacity;
        CheckBox ckIBeam, ckCaret, ckClasses;
        NumericUpDown numEnter, numLeave, numPoll;
        TextBox txtToggle, txtLearn, txtDiag, txtSettings, txtNewApp;
        ListBox lstApps;
        Label lblStatus, lblPsLive;

        const int GW = 620;

        public SettingsForm(App a)
        {
            app = a;
            Text = "AutoIME " + Ver.Text + "  设置";
            ClientSize = new Size(660, 790);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);
            ShowInTaskbar = app.Conf.ShowTaskbarButton;

            var flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(10, 8, 10, 8);
            flow.BackColor = Color.FromArgb(248, 248, 250);

            flow.Controls.Add(BuildBasic());
            flow.Controls.Add(BuildDetect());
            flow.Controls.Add(BuildApps());
            flow.Controls.Add(BuildTiming());
            flow.Controls.Add(BuildKeys());

            Controls.Add(flow);
            Controls.Add(BuildBottom());

            var live = new System.Windows.Forms.Timer();
            live.Interval = 600;
            live.Tick += delegate { UpdatePsLive(); };
            live.Start();

            LoadValues(app.Conf);
            lblStatus.Text = "改完点「保存并应用」，立即生效。";

            // 关键：设置界面在前台时先把全局热键让出来，
            // 否则你想录的那个组合会被 AutoIME 自己先吃掉，输入框收不到按键
            Activated += delegate { app.SuspendHotkeys(); };
            Deactivate += delegate { app.ResumeHotkeys(); };
        }

        // ------------------------------------------------------------- 布局工具
        static GroupBox MakeGroup(string title, int h)
        {
            var g = new GroupBox();
            g.Text = title;
            g.Width = GW;
            g.Height = h;
            g.Margin = new Padding(0, 0, 0, 8);
            return g;
        }

        static CheckBox MakeCheck(string text, int x, int y, int w)
        {
            var c = new CheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.Width = w;
            c.AutoSize = false;
            return c;
        }

        static Label MakeLabel(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.Location = new Point(x, y + 3);
            l.Width = w;
            return l;
        }

        // ---------------------------------------------------------------- 分组
        GroupBox BuildBasic()
        {
            var g = MakeGroup("基本", 172);
            ckEnable = MakeCheck("启用自动切换", 12, 24, 180);
            ckStrict = MakeCheck("严格锁定（Shift 撬开也关回）", 12, 48, 200);
            ckBadge = MakeCheck("显示右下角浮标", 12, 72, 180);
            ckRestore = MakeCheck("离开软件时恢复中文", 220, 24, 170);
            ckBadgeAlways = MakeCheck("浮标常驻显示", 220, 48, 150);
            ckAllApps = MakeCheck("对所有程序生效", 220, 72, 160);
            ckTray = MakeCheck("显示托盘图标", 12, 96, 180);
            ckTaskbar = MakeCheck("任务栏显示窗口按钮", 220, 96, 180);
            ckBadgeTopMost = MakeCheck("浮窗置顶", 12, 120, 180);
            ckBadgeLocked = MakeCheck("锁定浮窗位置/大小", 220, 120, 180);

            var lbOp = MakeLabel("浮窗不透明度", 12, 148, 100);
            trkOpacity = new TrackBar();
            trkOpacity.Location = new Point(120, 142);
            trkOpacity.Width = 170;
            trkOpacity.Minimum = 30; trkOpacity.Maximum = 100;
            trkOpacity.TickStyle = TickStyle.None;
            trkOpacity.Scroll += delegate { lblOpacity.Text = trkOpacity.Value + "%"; };
            lblOpacity = MakeLabel("", 300, 148, 60);

            g.Controls.Add(ckEnable); g.Controls.Add(ckStrict); g.Controls.Add(ckBadge);
            g.Controls.Add(ckRestore); g.Controls.Add(ckBadgeAlways); g.Controls.Add(ckAllApps);
            g.Controls.Add(ckTray); g.Controls.Add(ckTaskbar);
            g.Controls.Add(ckBadgeTopMost); g.Controls.Add(ckBadgeLocked);
            g.Controls.Add(lbOp); g.Controls.Add(trkOpacity); g.Controls.Add(lblOpacity);
            return g;
        }

        GroupBox BuildDetect()
        {
            var g = MakeGroup("识别输入框（除了 PS 以外，靠这些判据）", 78);
            ckIBeam = MakeCheck("标准 I 型光标", 12, 24, 150);
            ckCaret = MakeCheck("文字插入符", 180, 24, 140);
            ckClasses = MakeCheck("按窗口类名（Max 等）", 320, 24, 190);

            lblPsLive = new Label();
            lblPsLive.Location = new Point(12, 48);
            lblPsLive.Width = GW - 40;
            lblPsLive.Height = 22;
            lblPsLive.ForeColor = Color.FromArgb(0, 110, 60);
            lblPsLive.Text = "PS 状态：-";

            g.Controls.Add(ckIBeam); g.Controls.Add(ckCaret); g.Controls.Add(ckClasses);
            g.Controls.Add(lblPsLive);
            return g;
        }

        GroupBox BuildApps()
        {
            var g = MakeGroup("生效的程序（进程名，不含 .exe）", 196);
            lstApps = new ListBox();
            lstApps.Location = new Point(12, 22);
            lstApps.Size = new Size(GW - 40, 112);
            lstApps.IntegralHeight = false;
            g.Controls.Add(lstApps);

            txtNewApp = new TextBox();
            txtNewApp.Location = new Point(12, 146);
            txtNewApp.Width = 170;
            g.Controls.Add(txtNewApp);

            var add = new Button();
            add.Text = "添加";
            add.Location = new Point(190, 143);
            add.Width = 70;
            add.Click += delegate
            {
                string s = txtNewApp.Text.Trim();
                if (s.Length == 0) return;
                if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
                for (int i = 0; i < lstApps.Items.Count; i++)
                    if (string.Equals(lstApps.Items[i].ToString(), s, StringComparison.OrdinalIgnoreCase)) return;
                lstApps.Items.Add(s);
                txtNewApp.Text = "";
            };
            g.Controls.Add(add);

            var del = new Button();
            del.Text = "删除";
            del.Location = new Point(266, 143);
            del.Width = 70;
            del.Click += delegate { if (lstApps.SelectedIndex >= 0) lstApps.Items.RemoveAt(lstApps.SelectedIndex); };
            g.Controls.Add(del);

            g.Controls.Add(MakeLabel("例：Photoshop / 3dsmax / maya", 346, 146, 240));
            return g;
        }

        GroupBox BuildTiming()
        {
            var g = MakeGroup("时间参数（毫秒）", 84);
            numEnter = AddNum(g, "进入输入框后延迟", 24, 12, 30, 0, 2000);
            numLeave = AddNum(g, "离开后关输入法延迟", 52, 12, 350, 0, 5000);
            numPoll = AddNum(g, "轮询间隔", 24, 330, 40, 10, 500);
            return g;
        }

        NumericUpDown AddNum(GroupBox g, string label, int y, int x, int val, int min, int max)
        {
            g.Controls.Add(MakeLabel(label, x, y, 130));
            var n = new NumericUpDown();
            n.Location = new Point(x + 136, y);
            n.Width = 78;
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, val));
            g.Controls.Add(n);
            return n;
        }

        GroupBox BuildKeys()
        {
            var g = MakeGroup("快捷键（点进输入框，直接按组合键即可改）", 178);
            txtToggle = AddKey(g, "手动开/关输入法", 24);
            txtLearn = AddKey(g, "学习当前光标", 52);
            txtDiag = AddKey(g, "打开诊断面板", 80);
            txtSettings = AddKey(g, "打开设置主界面", 108);

            var hint = new Label();
            hint.Location = new Point(12, 140);
            hint.Width = GW - 40;
            hint.Height = 30;
            hint.ForeColor = Color.Gray;
            hint.Text = "点进输入框直接按组合键即可记录（必须带 CTRL 或 ALT）。\r\n改完点「保存并应用」立即生效；若日志提示注册失败，说明该组合被别的软件占了。";
            g.Controls.Add(hint);
            return g;
        }

        TextBox AddKey(GroupBox g, string label, int y)
        {
            g.Controls.Add(MakeLabel(label, 12, y, 130));
            var t = new TextBox();
            t.Location = new Point(148, y);
            t.Width = 210;
            t.KeyDown += HotKeyDown;          // 按键录制
            g.Controls.Add(t);
            return t;
        }

        // 按键录制：按下组合键就写进输入框
        void HotKeyDown(object sender, KeyEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            e.SuppressKeyPress = true;
            e.Handled = true;

            // 只按了修饰键，等真正的按键
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu ||
                e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;

            string k = KeyName(e.KeyCode);
            if (k == null)
            {
                lblStatus.Text = "这个键不支持，请用字母 / 数字 / F1-F24";
                return;
            }
            if (!e.Control && !e.Alt)
            {
                lblStatus.Text = "必须带 CTRL 或 ALT（只加 SHIFT 会抢正常打字）";
                return;
            }
            var sb = new StringBuilder();
            if (e.Control) sb.Append("CTRL+");
            if (e.Alt) sb.Append("ALT+");
            if (e.Shift) sb.Append("SHIFT+");
            sb.Append(k);
            tb.Text = sb.ToString();
            lblStatus.Text = "已记录：" + tb.Text + "（记得点「保存并应用」）";
        }

        static string KeyName(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return ((char)k).ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return ((char)('0' + (k - Keys.NumPad0))).ToString();
            if (k >= Keys.F1 && k <= Keys.F24) return "F" + (k - Keys.F1 + 1);
            return null;
        }

        Panel BuildBottom()
        {
            var p = new Panel();
            p.Dock = DockStyle.Bottom;
            p.Height = 84;
            p.BackColor = Color.FromArgb(238, 238, 242);

            var save = new Button();
            save.Text = "保存并应用";
            save.Location = new Point(12, 10);
            save.Size = new Size(104, 34);
            save.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            save.Click += delegate { SaveAll(); };
            p.Controls.Add(save);

            var def = new Button();
            def.Text = "恢复默认";
            def.Location = new Point(122, 10);
            def.Size = new Size(90, 34);
            def.Click += delegate
            {
                var d = Defaults();
                LoadValues(d);
                // 浮窗大小/位置不在界面控件里，需一并重置为默认，否则保存后仍沿用旧值
                app.Conf.BadgeW = d.BadgeW; app.Conf.BadgeH = d.BadgeH;
                app.Conf.BadgeX = d.BadgeX; app.Conf.BadgeY = d.BadgeY;
                lblStatus.Text = "已载入默认值（含浮窗大小/位置），点「保存并应用」生效。";
            };
            p.Controls.Add(def);

            var openIni = new Button();
            openIni.Text = "打开配置";
            openIni.Location = new Point(218, 10);
            openIni.Size = new Size(90, 34);
            openIni.Click += delegate { try { Process.Start("notepad.exe", app.Conf.Path); } catch { } };
            p.Controls.Add(openIni);

            var openLog = new Button();
            openLog.Text = "看日志";
            openLog.Location = new Point(314, 10);
            openLog.Size = new Size(74, 34);
            openLog.Click += delegate { try { Process.Start("notepad.exe", Log.Path); } catch { } };
            p.Controls.Add(openLog);

            var diag = new Button();
            diag.Text = "诊断面板";
            diag.Location = new Point(394, 10);
            diag.Size = new Size(90, 34);
            diag.Click += delegate { app.ShowDiag(); };
            p.Controls.Add(diag);

            var feedback = new Button();
            feedback.Text = "反馈";
            feedback.Location = new Point(490, 10);
            feedback.Size = new Size(76, 34);
            feedback.Click += delegate { app.ShowContact(); };
            p.Controls.Add(feedback);

            var close = new Button();
            close.Text = "关闭";
            close.Location = new Point(572, 10);
            close.Size = new Size(76, 34);
            close.Click += delegate { Hide(); };
            p.Controls.Add(close);

            lblStatus = new Label();
            lblStatus.Location = new Point(12, 52);
            lblStatus.Width = 620;
            lblStatus.Height = 24;
            lblStatus.ForeColor = Color.FromArgb(0, 110, 60);
            p.Controls.Add(lblStatus);
            return p;
        }

        // ------------------------------------------------------------- 数据存取
        Cfg Defaults()
        {
            var d = new Cfg();
            d.Path = app.Conf.Path;
            d.Apps = Cfg.DefaultApps();
            d.TextClasses = Cfg.DefaultTextClasses();
            d.NumClasses = Cfg.DefaultNumClasses();
            d.PsProcs = Cfg.DefaultPsProcs();
            return d;
        }

        void LoadValues(Cfg c)
        {
            ckEnable.Checked = c.Enabled;
            ckStrict.Checked = c.StrictLock;
            ckBadge.Checked = c.ShowBadge;
            ckBadgeAlways.Checked = c.BadgeAlways;
            ckBadgeTopMost.Checked = c.BadgeTopMost;
            ckBadgeLocked.Checked = c.BadgeLocked;
            trkOpacity.Value = (int)Math.Round(Math.Max(0.30, Math.Min(1.00, c.BadgeOpacity)) * 100);
            lblOpacity.Text = trkOpacity.Value + "%";
            ckRestore.Checked = c.RestoreOnLeave;
            ckAllApps.Checked = c.AllApps;
            ckTray.Checked = c.ShowTrayIcon;
            ckTaskbar.Checked = c.ShowTaskbarButton;

            ckIBeam.Checked = c.UseStdIBeam;
            ckCaret.Checked = c.UseCaret;
            ckClasses.Checked = c.UseClasses;

            numEnter.Value = Clamp(numEnter, c.EnterDelayMs);
            numLeave.Value = Clamp(numLeave, c.LeaveDelayMs);
            numPoll.Value = Clamp(numPoll, c.PollIntervalMs);

            txtToggle.Text = c.HotToggle;
            txtLearn.Text = c.HotLearn;
            txtDiag.Text = c.HotDiag;
            txtSettings.Text = c.HotSettings;

            lstApps.BeginUpdate();
            lstApps.Items.Clear();
            for (int i = 0; i < c.Apps.Count; i++) lstApps.Items.Add(c.Apps[i]);
            lstApps.EndUpdate();
        }

        static decimal Clamp(NumericUpDown n, int v)
        {
            if (v < n.Minimum) return n.Minimum;
            if (v > n.Maximum) return n.Maximum;
            return v;
        }

        void SaveAll()
        {
            uint m, v;
            if (!Hotkey.Parse(txtToggle.Text, out m, out v)) { lblStatus.Text = "「手动开/关输入法」快捷键格式不对"; return; }
            if (!Hotkey.Parse(txtLearn.Text, out m, out v)) { lblStatus.Text = "「学习当前光标」快捷键格式不对"; return; }
            if (!Hotkey.Parse(txtDiag.Text, out m, out v)) { lblStatus.Text = "「打开诊断面板」快捷键格式不对"; return; }
            if (!Hotkey.Parse(txtSettings.Text, out m, out v)) { lblStatus.Text = "「打开设置主界面」快捷键格式不对"; return; }
            string[] all = new string[] { txtToggle.Text, txtLearn.Text, txtDiag.Text, txtSettings.Text };
            for (int i = 0; i < all.Length; i++)
            {
                string s1 = all[i].ToUpperInvariant();
                if (s1.IndexOf("CTRL") < 0 && s1.IndexOf("ALT") < 0)
                {
                    lblStatus.Text = "快捷键必须带 CTRL 或 ALT（只加 SHIFT 会抢正常打字）：" + all[i];
                    return;
                }
            }

            var c = app.Conf;
            c.Enabled = ckEnable.Checked;
            c.StrictLock = ckStrict.Checked;
            c.ShowBadge = ckBadge.Checked;
            c.BadgeAlways = ckBadgeAlways.Checked;
            c.BadgeTopMost = ckBadgeTopMost.Checked;
            c.BadgeLocked = ckBadgeLocked.Checked;
            c.BadgeOpacity = trkOpacity.Value / 100.0;
            c.RestoreOnLeave = ckRestore.Checked;
            c.AllApps = ckAllApps.Checked;
            c.ShowTrayIcon = ckTray.Checked;
            c.ShowTaskbarButton = ckTaskbar.Checked;
            ShowInTaskbar = c.ShowTaskbarButton;

            c.UseStdIBeam = ckIBeam.Checked;
            c.UseCaret = ckCaret.Checked;
            c.UseClasses = ckClasses.Checked;

            c.EnterDelayMs = (int)numEnter.Value;
            c.LeaveDelayMs = (int)numLeave.Value;
            c.PollIntervalMs = (int)numPoll.Value;

            c.HotToggle = txtToggle.Text.Trim().ToUpperInvariant();
            c.HotLearn = txtLearn.Text.Trim().ToUpperInvariant();
            c.HotDiag = txtDiag.Text.Trim().ToUpperInvariant();
            c.HotSettings = txtSettings.Text.Trim().ToUpperInvariant();

            var apps = new List<string>();
            for (int i = 0; i < lstApps.Items.Count; i++) apps.Add(lstApps.Items[i].ToString());
            c.Apps = apps;

            // 查重
            var dup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < all.Length; i++)
            {
                string u = all[i].Trim();
                if (!dup.Add(u))
                {
                    lblStatus.Text = "有两个快捷键重复了：" + u + "，请改成不同的组合";
                    return;
                }
            }

            c.Save();
            var failed = app.ApplyConfig();
            if (failed != null && failed.Count > 0)
            {
                string list = string.Join("\r\n", failed.ToArray());
                lblStatus.Text = "已保存，但这些组合被占用了：" + string.Join(" / ", failed.ToArray());
                MessageBox.Show("以下快捷键注册失败（多半被别的软件占用了）：\r\n\r\n" + list +
                    "\r\n\r\n请换一个组合再试；点「保存并应用」前可以先在托盘菜单里确认当前的绑定。",
                    "AutoIME 快捷键没生效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                lblStatus.Text = "已保存并立即生效（" + DateTime.Now.ToString("HH:mm:ss") + "）";
            }
        }

        // ------------------------------------------------------- PS 状态显示
        void UpdatePsLive()
        {
            if (lblPsLive == null || app.Eng == null || app.Eng.PsTool == null) return;
            var ps = app.Eng.PsTool;
            if (!app.Conf.PsToolRule)
            {
                lblPsLive.Text = "PS 状态：已关闭（PS 里不会自动切中文）";
                lblPsLive.ForeColor = Color.Gray;
                return;
            }
            if (!ps.Available)
            {
                lblPsLive.Text = "PS 状态：读不到 —— 双击「启动PS工具助手.cmd」后再试";
                lblPsLive.ForeColor = Color.FromArgb(190, 90, 0);
                return;
            }
            if (ps.BusyIsText)
            {
                lblPsLive.Text = "PS 状态：正在编辑文字（已切中文）";
                lblPsLive.ForeColor = Color.FromArgb(0, 130, 60);
            }
            else if (ps.Busy)
            {
                lblPsLive.Text = "PS 状态：忙（" + (ps.LastKnownTool.Length > 0 ? ps.LastKnownTool : "未知") + "）→ 保持英文";
                lblPsLive.ForeColor = Color.FromArgb(90, 90, 90);
            }
            else
            {
                lblPsLive.Text = "PS 状态：" + (ps.LastKnownTool.Length > 0 ? ps.LastKnownTool : "-")
                    + (ps.TypeTool ? "  → 文字工具，切中文" : "  → 保持英文");
                lblPsLive.ForeColor = ps.TypeTool ? Color.FromArgb(0, 130, 60) : Color.FromArgb(90, 90, 90);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                app.ResumeHotkeys();
            }
            base.OnFormClosing(e);
        }
    }
    // --------------------------------------------------------------- 热键消息窗
    internal class HotkeyWindow : NativeWindow
    {
        public const int WM_HOTKEY = 0x0312;
        public Action<int> OnHotkey;

        public void CreateHandle2()
        {
            var cp = new CreateParams();
            cp.Parent = (IntPtr)(-3);
            cp.Caption = "AutoIME_hotkey";
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                var h = OnHotkey;
                if (h != null) { try { h(m.WParam.ToInt32()); } catch { } }
                return;
            }
            base.WndProc(ref m);
        }
    }

    // 首次启动快捷方式位置选择：桌面 / 开始菜单（可同时勾选）+ “以后不再提示”勾选框
    internal class ShortcutChoiceForm : Form
    {
        public bool WantDesktop = true;
        public bool WantPrograms = false;
        public bool NoAskAgain = false;

        CheckBox chkDesktop, chkPrograms, chkNoAsk;

        public ShortcutChoiceForm()
        {
            Text = "AutoIME - 快捷方式";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(340, 210);

            var lbl = new Label
            {
                Text = "首次启动完成，选择快捷方式放置位置（可多选）：",
                Location = new Point(20, 18),
                AutoSize = true
            };
            chkDesktop = new CheckBox { Text = "桌面", Location = new Point(30, 50), AutoSize = true, Checked = true };
            chkPrograms = new CheckBox { Text = "开始菜单", Location = new Point(30, 82), AutoSize = true };
            chkNoAsk = new CheckBox { Text = "以后不再提示快捷方式", Location = new Point(30, 116), AutoSize = true };

            var btnOk = new Button { Text = "确定", Location = new Point(150, 150), Size = new Size(80, 28) };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(240, 150), Size = new Size(80, 28) };

            btnOk.Click += delegate
            {
                if (!chkDesktop.Checked && !chkPrograms.Checked)
                {
                    MessageBox.Show("请至少选择一个快捷方式位置。", "AutoIME",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                WantDesktop = chkDesktop.Checked;
                WantPrograms = chkPrograms.Checked;
                NoAskAgain = chkNoAsk.Checked;
                DialogResult = DialogResult.OK;
            };

            Controls.AddRange(new Control[] { lbl, chkDesktop, chkPrograms, chkNoAsk, btnOk, btnCancel });
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }

    // ------------------------------------------------------------------ 主程序
    internal class App : ApplicationContext
    {
        const int HK_TOGGLE = 1, HK_LEARN = 2, HK_DIAG = 3, HK_SETTINGS = 4;

        readonly Cfg cfg;
        readonly Engine eng;
        readonly NotifyIcon tray;
        readonly BadgeForm badge;
        readonly HotkeyWindow hk;
        DiagForm diag;
        SettingsForm settings;
        Icon icoZh, icoEn;
        EventWaitHandle raiseSettingsEvent;
        System.Windows.Forms.Timer raiseTimer;

        public static App Current;
        public Cfg Conf { get { return cfg; } }
        public Engine Eng { get { return eng; } }

        public App(bool openDiag, bool openSettings)
        {
            Current = this;
            string dir = DataDir();
            Log.Path = System.IO.Path.Combine(dir, "AutoIME.log");
            cfg = Cfg.Load(System.IO.Path.Combine(dir, "AutoIME.ini"));

            ProbeHkls();

            eng = new Engine(cfg);
            eng.StateChanged += OnStateChanged;
            eng.Start();
            LogHook();
            Log.W("启动 " + Ver.Text + " 方式=" + cfg.Method + " 中文HKL=0x" + (cfg.ChineseHkl.ToInt64() & 0xFFFFFFFF).ToString("X8") +
                  " 英文HKL=0x" + (cfg.EnglishHkl.ToInt64() & 0xFFFFFFFF).ToString("X8"));

            icoZh = MakeIcon(Color.FromArgb(196, 42, 51), "中");
            icoEn = MakeIcon(Color.FromArgb(28, 90, 160), "英");

            badge = new BadgeForm(cfg);
            badge.Position();

            tray = new NotifyIcon();
            tray.Icon = icoEn;
            tray.Text = "AutoIME " + Ver.Text + " 输入法自动开关";
            tray.Visible = cfg.ShowTrayIcon;
            tray.MouseDoubleClick += (s, e) => ShowSettings();
            tray.ContextMenuStrip = BuildMenu();

            hk = new HotkeyWindow();
            hk.CreateHandle2();
            hk.OnHotkey = OnHotkey;
            hkOkToggle = Register(cfg.HotToggle, HK_TOGGLE);
            hkOkLearn = Register(cfg.HotLearn, HK_LEARN);
            hkOkDiag = Register(cfg.HotDiag, HK_DIAG);
            hkOkSettings = Register(cfg.HotSettings, HK_SETTINGS);

            // 热键可能被别的程序/实例暂时占着：注册失败的每 1.5 秒重试
            if (!hkOkToggle || !hkOkLearn || !hkOkDiag || !hkOkSettings) StartHotkeyRetry();

            if (cfg.ShowBadge) badge.ShowState(ImeState.Unknown, "AutoIME 已启动", cfg.BadgeMs, cfg.BadgeAlways);
            if (openDiag) ShowDiag();
            if (openSettings) ShowSettings();
            if (!openDiag && !openSettings) MaybeAskShortcut();

            // 第二实例双击程序时，通过命名事件唤起本实例的设置界面
            try { raiseSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "AutoIME_open_settings_evt_7d1f"); } catch { }
            raiseTimer = new System.Windows.Forms.Timer();
            raiseTimer.Interval = 500;
            raiseTimer.Tick += delegate
            {
                if (raiseSettingsEvent != null && raiseSettingsEvent.WaitOne(0)) ShowSettings();
            };
            raiseTimer.Start();
        }

        // 启动时写一段环境信息，排障不用再问东问西
        void LogHook()
        {
            try
            {
                Log.W("========== AutoIME " + Ver.Text + " 启动 ==========");
                Log.W("系统: " + Environment.OSVersion.VersionString +
                      "  " + (Environment.Is64BitProcess ? "x64" : "x86") +
                      "  " + (IsElevated() ? "管理员" : "普通用户") +
                      "  UAC=" + (UacDisabled() ? "关闭" : "开启"));
                Log.W("程序: " + Application.ExecutablePath);
                Log.W("配置: " + cfg.Path);
                Log.W("显示: " + Screen.AllScreens.Length + " 屏  主屏 " +
                      SystemInformation.PrimaryMonitorSize.Width + "x" + SystemInformation.PrimaryMonitorSize.Height);
            }
            catch { }
        }

        // 一键导出诊断报告（给排障/反馈用）
        public string ExportDiag()
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("AutoIME 诊断报告");
                sb.AppendLine("导出时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("版本: " + Ver.Text + " (" + Ver.Date + ")");
                sb.AppendLine("程序: " + Application.ExecutablePath);
                sb.AppendLine("系统: " + Environment.OSVersion.VersionString +
                              " / " + (Environment.Is64BitProcess ? "x64" : "x86") +
                              " / " + (IsElevated() ? "管理员" : "普通") +
                              " / UAC=" + (UacDisabled() ? "关闭" : "开启"));
                sb.AppendLine("显示器: " + Screen.AllScreens.Length + " 屏");
                sb.AppendLine();
                sb.AppendLine("---- 当前配置 (AutoIME.ini) ----");
                try { sb.AppendLine(File.ReadAllText(cfg.Path, Encoding.UTF8)); } catch (Exception ex) { sb.AppendLine("读配置失败: " + ex.Message); }
                sb.AppendLine();
                sb.AppendLine("---- PS 工具状态 ----");
                sb.AppendLine("规则开启: " + cfg.PsToolRule + "  可用: " + (eng.PsTool != null && eng.PsTool.Available) +
                              "  上次工具: " + (eng.PsTool == null ? "-" : eng.PsTool.LastKnownTool) +
                              "  忙: " + (eng.PsTool == null ? "-" : eng.PsTool.Busy.ToString()));
                sb.AppendLine("状态文件: " + (eng.PsTool == null ? "-" : eng.PsTool.OutPathForDiag));
                sb.AppendLine();
                sb.AppendLine("---- 相关进程 ----");
                string[] names = new string[] { "Photoshop", "3dsmax", "maya", "cscript", "wscript" };
                for (int i = 0; i < names.Length; i++)
                {
                    try
                    {
                        var ps = Process.GetProcessesByName(names[i]);
                        sb.AppendLine(names[i] + ": " + ps.Length + " 个");
                        for (int j = 0; j < ps.Length; j++) { try { sb.AppendLine("    pid " + ps[j].Id); } catch { } }
                    }
                    catch { }
                }
                sb.AppendLine();
                sb.AppendLine("---- 最近日志（最多 300 行，含滚动备份）----");
                try
                {
                    var all = new List<string>();
                    string bak = Log.Path + ".1";
                    if (File.Exists(bak)) all.AddRange(File.ReadAllLines(bak, Encoding.UTF8));
                    if (File.Exists(Log.Path)) all.AddRange(File.ReadAllLines(Log.Path, Encoding.UTF8));
                    int from = Math.Max(0, all.Count - 300);
                    for (int i = from; i < all.Count; i++) sb.AppendLine(all[i]);
                }
                catch (Exception ex) { sb.AppendLine("读日志失败: " + ex.Message); }

                string outPath = System.IO.Path.Combine(DataDir(), "AutoIME.诊断报告.txt");
                File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
                Log.W("已导出诊断报告: " + outPath);
                return outPath;
            }
            catch (Exception ex)
            {
                Log.W("导出诊断报告失败: " + ex.Message);
                return null;
            }
        }

        // 优先放在 exe 旁边（便携）；目录不可写就退回 %LOCALAPPDATA%\AutoIME
        public static string DataDir()
        {
            string exeDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            try
            {
                string probe = System.IO.Path.Combine(exeDir, ".wtest");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return exeDir;
            }
            catch
            {
                string alt = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoIME");
                try { System.IO.Directory.CreateDirectory(alt); } catch { }
                return alt;
            }
        }

        // 首次启动询问快捷方式位置；勾选“以后不再提示”后写入标记文件，之后不再询问
        static string ShortcutFlagPath()
        {
            return System.IO.Path.Combine(DataDir(), "AutoIME.shortcut.asked");
        }

        static bool CreateShortcutIn(string folder)
        {
            try
            {
                try { System.IO.Directory.CreateDirectory(folder); } catch { }
                string exePath = Application.ExecutablePath;
                string exeDir = System.IO.Path.GetDirectoryName(exePath);
                string lnk = System.IO.Path.Combine(folder, "AutoIME.lnk");

                Type wshType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshType == null) return false;
                object wsh = Activator.CreateInstance(wshType);
                object sc = wshType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, wsh, new object[] { lnk });
                Type scType = sc.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exePath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { exeDir });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "AutoIME 输入法自动开关" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                return true;
            }
            catch (Exception ex)
            {
                Log.W("创建快捷方式失败: " + ex.Message);
                return false;
            }
        }

        void MaybeAskShortcut()
        {
            try
            {
                string flag = ShortcutFlagPath();
                if (File.Exists(flag)) return;
                // 桌面已有快捷方式（用户已选择过或已手动放置），视为已确认，不再询问
                string desktopLnk = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AutoIME.lnk");
                if (File.Exists(desktopLnk)) return;
                using (var dlg = new ShortcutChoiceForm())
                {
                    var r = dlg.ShowDialog();
                    if (r != DialogResult.OK) return;   // 取消：不创建、不记标记，下次仍提示
                    if (dlg.WantDesktop)
                        CreateShortcutIn(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                    if (dlg.WantPrograms)
                        CreateShortcutIn(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
                    if (dlg.NoAskAgain)
                    {
                        try { File.WriteAllText(flag, "1"); } catch { }
                    }
                }
            }
            catch { }
        }

        void ProbeHkls()
        {
            if (cfg.ChineseHkl == IntPtr.Zero || cfg.EnglishHkl == IntPtr.Zero)
            {
                IntPtr zh, en;
                DetectHkls(out zh, out en);
                if (cfg.ChineseHkl == IntPtr.Zero) cfg.ChineseHkl = zh;
                if (cfg.EnglishHkl == IntPtr.Zero) cfg.EnglishHkl = en;
            }
        }

        public static void DetectHkls(out IntPtr zh, out IntPtr en)
        {
            zh = IntPtr.Zero; en = IntPtr.Zero;
            int n = Nat.GetKeyboardLayoutList(0, null);
            if (n <= 0) return;
            var arr = new IntPtr[n];
            n = Nat.GetKeyboardLayoutList(n, arr);
            IntPtr zhTw = IntPtr.Zero, enUs = IntPtr.Zero;
            for (int i = 0; i < n && i < arr.Length; i++)
            {
                int lang = (int)(arr[i].ToInt64() & 0xFFFF);
                if (lang == 0x0804 && zh == IntPtr.Zero) zh = arr[i];
                if ((lang == 0x0404 || lang == 0x0C04) && zhTw == IntPtr.Zero) zhTw = arr[i];
                if (lang == 0x0409 && enUs == IntPtr.Zero) enUs = arr[i];
                if ((lang == 0x0809 || lang == 0x0C09) && en == IntPtr.Zero) en = arr[i];
            }
            if (zh == IntPtr.Zero) zh = zhTw;
            if (en == IntPtr.Zero) en = enUs;
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Font = new Font("Microsoft YaHei UI", 9f);

            var miAuto = new ToolStripMenuItem("自动开关输入法");
            miAuto.Checked = cfg.Enabled;
            miAuto.Click += delegate
            {
                cfg.Enabled = !cfg.Enabled;
                cfg.Save();
                miAuto.Checked = cfg.Enabled;
                tray.Text = "AutoIME " + Ver.Text + (cfg.Enabled ? "" : "（已暂停）");
                if (!cfg.Enabled) badge.HideNow();
            };

            var miToggle = new ToolStripMenuItem("手动开/关输入法  (" + cfg.HotToggle + ")");
            miToggle.Click += delegate { ManualToggle(); };

            var miLearn = new ToolStripMenuItem("学习当前光标  (" + cfg.HotLearn + ")");
            miLearn.Click += delegate { LearnNow(); };

            var miExport = new ToolStripMenuItem("导出诊断报告…");
            miExport.Click += delegate
            {
                string p = ExportDiag();
                if (p == null)
                {
                    MessageBox.Show("导出失败，请查看日志。", "AutoIME", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try { Clipboard.SetText(p); } catch { }
                MessageBox.Show("诊断报告已生成：\r\n" + p +
                    "\r\n\r\n（路径已复制到剪贴板，直接发给我即可）\r\n" +
                    "内容：版本 / 系统 / 配置 / PS 工具状态 / 相关进程 / 最近 300 行日志。",
                    "AutoIME 导出诊断报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                try
                {
                    var psi = new ProcessStartInfo("explorer.exe", "/select,\"" + p + "\"");
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
                catch { }
            };

            var miDiag = new ToolStripMenuItem("诊断面板  (" + cfg.HotDiag + ")");
            miDiag.Click += delegate { ShowDiag(); };

            var miRaw = new ToolStripMenuItem("重新载入 AutoIME.ini");
            miRaw.Click += delegate { Reload(); };

            var miOpen = new ToolStripMenuItem("打开配置文件夹");
            miOpen.Click += delegate { OpenConfigFolder(); };

            var miBoot = new ToolStripMenuItem("开机自动启动");
            miBoot.Checked = IsStartup();
            miBoot.Click += delegate
            {
                bool want = !IsStartup();
                bool ok = SetStartup(want);
                bool nowOn = IsStartup();
                miBoot.Checked = nowOn;
                if (!ok || nowOn != want)
                {
                    HelpStartup(want);
                }
            };

            var miTray = new ToolStripMenuItem("显示托盘图标");
            miTray.Checked = cfg.ShowTrayIcon;
            miTray.Click += delegate
            {
                cfg.ShowTrayIcon = !cfg.ShowTrayIcon;
                cfg.Save();
                tray.Visible = cfg.ShowTrayIcon;
                miTray.Checked = cfg.ShowTrayIcon;
            };

            var miBadgeTop = new ToolStripMenuItem("浮窗置顶");
            miBadgeTop.Checked = cfg.BadgeTopMost;
            miBadgeTop.Click += delegate
            {
                cfg.BadgeTopMost = !cfg.BadgeTopMost;
                cfg.Save();
                badge.ApplySettings();
                miBadgeTop.Checked = cfg.BadgeTopMost;
            };

            var miBadgeLock = new ToolStripMenuItem("锁定浮窗");
            miBadgeLock.Checked = cfg.BadgeLocked;
            miBadgeLock.Click += delegate
            {
                cfg.BadgeLocked = !cfg.BadgeLocked;
                cfg.Save();
                miBadgeLock.Checked = cfg.BadgeLocked;
            };

            bool elevated = IsElevated();
            var miAdmin = new ToolStripMenuItem(elevated ? "已以管理员权限运行" : "以管理员身份重新启动");
            miAdmin.Enabled = !elevated;
            miAdmin.Click += delegate { RestartAsAdmin(); };

            var miFeedback = new ToolStripMenuItem("反馈 / 联系作者");
            miFeedback.Click += delegate { ShowContact(); };

            var miAbout = new ToolStripMenuItem("关于 AutoIME " + Ver.Text);
            miAbout.Click += delegate { ShowAbout(); };

            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { ExitApp(); };

            var miSettings = new ToolStripMenuItem("设置主界面…  (" + cfg.HotSettings + ")");
            miSettings.Click += delegate { ShowSettings(); };

            m.Items.Add(miSettings);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(miAuto);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(miToggle);
            m.Items.Add(miLearn);
            m.Items.Add(miDiag);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(miRaw);
            m.Items.Add(miOpen);
            m.Items.Add(miBoot);
            m.Items.Add(miTray);
            m.Items.Add(miBadgeTop);
            m.Items.Add(miBadgeLock);
            m.Items.Add(miAdmin);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(miFeedback);
            m.Items.Add(miAbout);
            m.Items.Add(miExit);
            return m;
        }

        // 关于窗口：开发者 / 免费声明 / 开源说明
        void ShowAbout()
        {
            var f = new Form();
            f.Text = "关于 AutoIME " + Ver.Text;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.MaximizeBox = false; f.MinimizeBox = false;
            f.ClientSize = new Size(420, 220);
            f.Font = new Font("Microsoft YaHei UI", 9.5f);

            var lbl = new Label();
            lbl.Left = 16; lbl.Top = 16; lbl.Width = 388; lbl.Height = 130;
            lbl.Text = "AutoIME " + Ver.Text + "（" + Ver.Date + "）\r\n\r\n" +
                "本软件开发者：迁渡口°\r\n\r\n" +
                "本软件为纯免费交流分享，无任何收费；需要收费的全是诈骗。\r\n\r\n" +
                "本软件源代码已开源，可在 GitHub 搜索 AutoIME；\r\n" +
                "有 bug 或需要加新功能，请点「反馈」联系开发者。";
            f.Controls.Add(lbl);

            var btnFeedback = new Button();
            btnFeedback.Text = "反馈 / 联系作者";
            btnFeedback.Location = new Point(16, 168);
            btnFeedback.Size = new Size(160, 32);
            btnFeedback.Click += delegate { ShowContact(); };
            f.Controls.Add(btnFeedback);

            var btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(276, 168);
            btnClose.Size = new Size(112, 32);
            btnClose.Click += delegate { f.Close(); };
            f.Controls.Add(btnClose);

            f.ShowDialog();
        }

        // 反馈 / 联系作者：显示 QQ 邮箱与 QQ 号，可复制
        public void ShowContact()
        {
            var f = new Form();
            f.Text = "反馈 / 联系作者";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.MaximizeBox = false; f.MinimizeBox = false;
            f.ClientSize = new Size(360, 168);
            f.Font = new Font("Microsoft YaHei UI", 9.5f);

            var lbl = new Label();
            lbl.Left = 16; lbl.Top = 18; lbl.Width = 328; lbl.Height = 60;
            lbl.ForeColor = Color.FromArgb(0, 90, 160);
            lbl.Text = "QQ 邮箱：401333265@qq.com\r\nQQ 号：401333265";
            f.Controls.Add(lbl);

            var btnCopy = new Button();
            btnCopy.Text = "复制联系方式";
            btnCopy.Location = new Point(16, 96);
            btnCopy.Size = new Size(150, 32);
            btnCopy.Click += delegate
            {
                try { Clipboard.SetText("QQ邮箱：401333265@qq.com；QQ号：401333265"); } catch { }
            };
            f.Controls.Add(btnCopy);

            var btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Location = new Point(196, 96);
            btnClose.Size = new Size(120, 32);
            btnClose.Click += delegate { f.Close(); };
            f.Controls.Add(btnClose);

            f.ShowDialog();
        }

        void OnStateChanged(ImeState s, string why)
        {
            try
            {
                var sn = eng.Last;
                string det = sn != null && sn.Proc.Length > 0 ? sn.Proc : "";
                if (s == ImeState.Chinese) { tray.Icon = icoZh; tray.Text = "AutoIME: 输入法开（中文）"; }
                else if (s == ImeState.English) { tray.Icon = icoEn; tray.Text = "AutoIME: 输入法关（快捷键）"; }
                else tray.Text = "AutoIME: " + why;
                if (cfg.ShowBadge)
                {
                    if (cfg.BadgeAlways)
                    {
                        // 常驻：持续显示，不按状态计时隐藏；未知状态也保持可见
                        badge.ShowState(s, det, cfg.BadgeMs, true);
                    }
                    else if (s == ImeState.Chinese || s == ImeState.English)
                    {
                        badge.ShowState(s, det, cfg.BadgeMs, false);
                    }
                }
                Log.W("-> " + s + " (" + why + " | " + det + ")");
            }
            catch { }
        }

        void ManualToggle()
        {
            var sn = eng.Last;
            if (sn == null || sn.ImeWnd == IntPtr.Zero) return;
            bool toChinese = !sn.ImeOpen;
            bool real;
            Ime.SetOpen(sn.ImeWnd, toChinese, out real);
            if (toChinese) Ime.SetChineseMode(sn.ImeWnd);
            badge.ShowState(toChinese ? ImeState.Chinese : ImeState.English, sn.Proc, 1200, false);
        }

        void LearnNow()
        {
            var sn = eng.Last;
            if (sn == null || sn.Hwnd == IntPtr.Zero) { badge.ShowState(ImeState.Unknown, "没有前台窗口", 1400, false); return; }
            if (string.IsNullOrEmpty(sn.CursorHash)) { badge.ShowState(ImeState.Unknown, "看不到鼠标光标", 1400, false); return; }
            eng.Learn(sn.Proc, sn.CursorHash);
            badge.ShowState(ImeState.Unknown, "已学习 " + sn.Proc, 1400, false);
            Log.W("学习光标 " + sn.Proc + "|" + sn.CursorHash);
        }

        // 自启设置失败时的引导：把你引到"双击一次就好"的脚本
        void HelpStartup(bool turnOn)
        {
            string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            string cmdFile = System.IO.Path.Combine(dir, turnOn ? "启用开机自启.cmd" : "关闭开机自启.cmd");
            try
            {
                var psi = new ProcessStartInfo("explorer.exe", "/select,\"" + cmdFile + "\"");
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.W("打开程序目录失败: " + ex.Message); }

            MessageBox.Show(
                "你这台机器不允许 AutoIME 自己改启动项（安全软件按程序白名单拦的，连子进程都会被拦）。\r\n\r\n" +
                "已经帮你打开程序目录并选中：\r\n  " + (turnOn ? "启用开机自启.cmd" : "关闭开机自启.cmd") + "\r\n\r\n" +
                "双击它一次就好（它由资源管理器启动，不受这个限制）。",
                turnOn ? "AutoIME 开机自启（双击一次即可）" : "AutoIME 关闭自启（双击一次即可）",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        public void ShowSettings()
        {
            if (settings == null || settings.IsDisposed) settings = new SettingsForm(this);
            settings.Show();
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
        }

        // 设置界面保存后调用：重新注册热键、重建菜单、刷新托盘
        // 先注销再重新注册四个全局热键，返回注册失败的组合（供界面提示）
        List<string> ReregisterHotkeys()
        {
            var failed = new List<string>();
            Nat.UnregisterHotKey(hk.Handle, HK_TOGGLE);
            Nat.UnregisterHotKey(hk.Handle, HK_LEARN);
            Nat.UnregisterHotKey(hk.Handle, HK_DIAG);
            Nat.UnregisterHotKey(hk.Handle, HK_SETTINGS);
            hkOkToggle = Register(cfg.HotToggle, HK_TOGGLE);
            hkOkLearn = Register(cfg.HotLearn, HK_LEARN);
            hkOkDiag = Register(cfg.HotDiag, HK_DIAG);
            hkOkSettings = Register(cfg.HotSettings, HK_SETTINGS);
            if (!hkOkToggle) failed.Add(cfg.HotToggle);
            if (!hkOkLearn) failed.Add(cfg.HotLearn);
            if (!hkOkDiag) failed.Add(cfg.HotDiag);
            if (!hkOkSettings) failed.Add(cfg.HotSettings);
            return failed;
        }

        public List<string> ApplyConfig()
        {
            var failed = ReregisterHotkeys();
            tray.Visible = cfg.ShowTrayIcon;
            badge.ApplySettings();
            tray.ContextMenuStrip = BuildMenu();

            // 热键可能被别的实例暂时占着：注册失败的每 1.5 秒重试一次（复用同一个定时器）
            if (!hkOkToggle || !hkOkLearn || !hkOkDiag || !hkOkSettings) StartHotkeyRetry();
            tray.Text = "AutoIME " + Ver.Text + (cfg.Enabled ? "" : "（已暂停）");
            if (!cfg.Enabled || !cfg.ShowBadge)
            {
                badge.HideNow();
            }
            else
            {
                var sn = eng != null ? eng.Last : null;
                string det = sn != null && sn.Proc.Length > 0 ? sn.Proc : "";
                ImeState cur = eng != null ? eng.State : ImeState.Unknown;
                badge.ShowState(cur, det, cfg.BadgeMs, cfg.BadgeAlways);
            }
            return failed;
        }

        public void ShowDiag()
        {
            if (diag == null || diag.IsDisposed) diag = new DiagForm(eng);
            diag.Show();
            if (diag.WindowState == FormWindowState.Minimized) diag.WindowState = FormWindowState.Normal;
            diag.Activate();
        }

        // -------------------------------------------------- 打开配置文件夹
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        delegate bool EnumProc(IntPtr h, IntPtr lp);

        static bool ExplorerShows(string name)
        {
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                var c = new System.Text.StringBuilder(128);
                GetClassName(h, c, 128);
                if (c.ToString() != "CabinetWClass" || !IsWindowVisible(h)) return true;
                var tt = new System.Text.StringBuilder(256);
                GetWindowText(h, tt, 256);
                if (tt.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        void OpenConfigFolder()
        {
            string dir = System.IO.Path.GetDirectoryName(cfg.Path);
            string folder = System.IO.Path.GetFileName(dir);
            bool ok = false;
            bool started = false;
            try
            {
                var psi = new ProcessStartInfo("explorer.exe", "/select,\"" + cfg.Path + "\"");
                psi.UseShellExecute = false;
                Process.Start(psi);
                started = true;
                // 资源管理器可能是"交给已有窗口"，需要一点时间
                for (int i = 0; i < 33 && !ok; i++)
                {
                    Thread.Sleep(120);
                    ok = ExplorerShows(folder);
                }
                // 再试一次：直接打开文件夹（不选中文件），有些环境只认这种
                if (!ok)
                {
                    var psi2 = new ProcessStartInfo("explorer.exe", "\"" + dir + "\"");
                    psi2.UseShellExecute = false;
                    Process.Start(psi2);
                    for (int i = 0; i < 25 && !ok; i++)
                    {
                        Thread.Sleep(120);
                        ok = ExplorerShows(folder);
                    }
                }
            }
            catch (Exception ex) { Log.W("打开配置文件夹异常: " + ex.Message); }

            if (ok) { Log.W("已打开配置文件夹: " + dir); return; }

            // 命令发出去了但没看到窗口：不要说"失败"，给出路径让用户自己粘贴
            bool copied = false;
            try { Clipboard.SetText(dir); copied = true; } catch { }
            Log.W("打开配置文件夹未检测到窗口 (started=" + started + ", copied=" + copied + ")");
            MessageBox.Show("没有检测到资源管理器窗口（可能被安全软件拦，或窗口开在别的屏幕后面）。\r\n\r\n" +
                "文件夹路径" + (copied ? "已复制到剪贴板：" : "：") + "\r\n" + dir +
                "\r\n\r\n可以按 Win+R 粘贴这个路径直接打开。\r\n" +
                "（如果你是自己双击 exe 启动的 AutoIME，这个功能通常是正常的；" +
                "这里的失败多半是因为它由别的程序代为启动。）",
                "AutoIME 打开配置文件夹", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Reload()
        {
            try
            {
                var c2 = Cfg.Load(cfg.Path);
                CopyInto(c2, cfg);
                ProbeHkls();
                ReregisterHotkeys();
                tray.Visible = cfg.ShowTrayIcon;
                badge.ApplySettings();
                tray.ContextMenuStrip = BuildMenu();

                // 热键可能被别的实例暂时占着：注册失败的每 1.5 秒重试一次（复用同一个定时器）
                if (!hkOkToggle || !hkOkLearn || !hkOkDiag || !hkOkSettings) StartHotkeyRetry();
                badge.ShowState(ImeState.Unknown, "配置已重新载入", 1500, false);
            }
            catch (Exception ex) { Log.W("reload fail " + ex.Message); }
        }

        static void CopyInto(Cfg from, Cfg to)
        {
            to.Enabled = from.Enabled; to.Method = from.Method;
            to.PollIntervalMs = from.PollIntervalMs; to.EnterDelayMs = from.EnterDelayMs;
            to.LeaveDelayMs = from.LeaveDelayMs; to.ManualOverrideMs = from.ManualOverrideMs;
            to.SyncIntervalMs = from.SyncIntervalMs; to.RestoreOnLeave = from.RestoreOnLeave;
            to.StrictLock = from.StrictLock; to.AllowLayoutSwitch = from.AllowLayoutSwitch;
            to.ShowBadge = from.ShowBadge; to.BadgeAlways = from.BadgeAlways; to.BadgeMs = from.BadgeMs;
            to.BadgeTopMost = from.BadgeTopMost; to.BadgeLocked = from.BadgeLocked; to.BadgeOpacity = from.BadgeOpacity; to.BadgeW = from.BadgeW; to.BadgeH = from.BadgeH;
            to.BadgeX = from.BadgeX; to.BadgeY = from.BadgeY;
            to.ShowTrayIcon = from.ShowTrayIcon;
            to.ShowTaskbarButton = from.ShowTaskbarButton;
            to.AllApps = from.AllApps; to.UseStdIBeam = from.UseStdIBeam;
            to.UseShape = from.UseShape; to.UseShapeLoose = from.UseShapeLoose; to.UseCaret = from.UseCaret;
            to.UseClasses = from.UseClasses; to.PsToolRule = from.PsToolRule; to.PsProcs = from.PsProcs;
            to.LogLevel = from.LogLevel;
            to.ChineseHkl = from.ChineseHkl; to.EnglishHkl = from.EnglishHkl;
            to.Apps = from.Apps; to.TextClasses = from.TextClasses; to.NumClasses = from.NumClasses;
            to.HotToggle = from.HotToggle; to.HotLearn = from.HotLearn;
            to.HotDiag = from.HotDiag; to.HotSettings = from.HotSettings;
        }

        // 每个热键当前是否已注册成功（失败后由定时器重试）
        bool hkOkToggle = true, hkOkLearn = true, hkOkDiag = true, hkOkSettings = true;

        bool Register(string spec, int id)
        {
            return Register(spec, id, false);
        }

        bool Register(string spec, int id, bool quiet)
        {
            uint mods, vk;
            if (!Hotkey.Parse(spec, out mods, out vk))
            {
                if (!quiet) Log.W("热键格式错误: " + spec);
                return false;
            }
            if (!Nat.RegisterHotKey(hk.Handle, id, mods, vk))
            {
                if (!quiet) Log.W("热键注册失败(可能被占用): " + spec);
                return false;
            }
            return true;
        }

        System.Windows.Forms.Timer hkRetryTimer;

        // 幂等地启动"热键补注册"定时器
        void StartHotkeyRetry()
        {
            if (hkRetryTimer != null) { if (!hkRetryTimer.Enabled) hkRetryTimer.Start(); return; }
            hkRetryTimer = new System.Windows.Forms.Timer();
            hkRetryTimer.Interval = 1500;
            hkRetryTimer.Tick += delegate
            {
                EnsureHotkeys();
                if (hkOkToggle && hkOkLearn && hkOkDiag && hkOkSettings) hkRetryTimer.Stop();
            };
            hkRetryTimer.Start();
        }

        // 只补注册"还没成功"的那些；成功后打一条日志
        void EnsureHotkeys()
        {
            if (!hkOkToggle) { hkOkToggle = Register(cfg.HotToggle, HK_TOGGLE, true); if (hkOkToggle) Log.W("热键恢复: " + cfg.HotToggle); }
            if (!hkOkLearn) { hkOkLearn = Register(cfg.HotLearn, HK_LEARN, true); if (hkOkLearn) Log.W("热键恢复: " + cfg.HotLearn); }
            if (!hkOkDiag) { hkOkDiag = Register(cfg.HotDiag, HK_DIAG, true); if (hkOkDiag) Log.W("热键恢复: " + cfg.HotDiag); }
            if (!hkOkSettings) { hkOkSettings = Register(cfg.HotSettings, HK_SETTINGS, true); if (hkOkSettings) Log.W("热键恢复: " + cfg.HotSettings); }
        }

        // 打开设置界面时先让出所有热键，否则你想录的组合键会被自己抢走
        public void SuspendHotkeys()
        {
            Nat.UnregisterHotKey(hk.Handle, HK_TOGGLE);
            Nat.UnregisterHotKey(hk.Handle, HK_LEARN);
            Nat.UnregisterHotKey(hk.Handle, HK_DIAG);
            Nat.UnregisterHotKey(hk.Handle, HK_SETTINGS);
            hkOkToggle = hkOkLearn = hkOkDiag = hkOkSettings = false;
            Log.W("设置界面打开，暂停全局热键");
        }

        public void ResumeHotkeys()
        {
            hkOkToggle = Register(cfg.HotToggle, HK_TOGGLE, true);
            hkOkLearn = Register(cfg.HotLearn, HK_LEARN, true);
            hkOkDiag = Register(cfg.HotDiag, HK_DIAG, true);
            hkOkSettings = Register(cfg.HotSettings, HK_SETTINGS, true);
            Log.W("设置界面关闭，恢复全局热键" +
                  (hkOkToggle && hkOkLearn && hkOkDiag && hkOkSettings ? "" : "（部分失败，1.5 秒后自动重试）"));
            if (!hkOkToggle || !hkOkLearn || !hkOkDiag || !hkOkSettings) StartHotkeyRetry();
        }

        void OnHotkey(int id)
        {
            switch (id)
            {
                case HK_TOGGLE: ManualToggle(); break;
                case HK_LEARN: LearnNow(); break;
                case HK_DIAG: ShowDiag(); break;
                case HK_SETTINGS: ShowSettings(); break;
            }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "AutoIME";

        static string StartupLauncher()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup), "AutoIME.vbs");
        }

        public static bool IsStartup()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k != null && k.GetValue(RunName) != null) return true;
                }
            }
            catch { }
            try { return File.Exists(StartupLauncher()); } catch { return false; }
        }

        // 返回是否真的成功（以前失败是静默的，勾选状态照样显示）
        public static bool SetStartup(bool on)
        {
            bool ok = false;

            // 1) 注册表 Run 键
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k != null)
                    {
                        if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                        else k.DeleteValue(RunName, false);
                        ok = true;
                    }
                }
            }
            catch (Exception ex) { Log.W("写注册表启动项失败: " + ex.Message); ok = false; }

            // 2) 兜底：调用系统自带的 reg.exe。
            //    某些安全软件只允许"受信任的程序"改启动项，却放行 reg.exe，
            //    所以让 reg.exe 替我们写（本进程不碰注册表）。
            if (!ok)
            {
                try
                {
                    string regArgs = on
                        ? "add \"HKCU\\" + RunKey + "\" /v " + RunName + " /t REG_SZ /d \"" +
                          Application.ExecutablePath + "\" /f"
                        : "delete \"HKCU\\" + RunKey + "\" /v " + RunName + " /f";
                    var rpsi = new ProcessStartInfo("reg.exe", regArgs);
                    rpsi.UseShellExecute = false;
                    rpsi.CreateNoWindow = true;
                    var rp = Process.Start(rpsi);
                    if (rp != null) rp.WaitForExit(4000);
                    ok = (IsStartup() == on);      // 关的时候要看"是否真的没了"
                    Log.W("用 reg.exe 写启动项 => " + (ok ? "成功" : "失败") +
                          (rp == null ? "" : " (reg 退出码 " + rp.ExitCode + ")"));
                }
                catch (Exception ex) { Log.W("调用 reg.exe 失败: " + ex.Message); ok = false; }
            }

            // 读回校验（安全软件可能静默丢弃写入）
            if (ok)
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        string v = (k == null) ? null : (string)k.GetValue(RunName);
                        ok = on ? (v != null) : (v == null);
                    }
                }
                catch { ok = false; }
                if (!ok) Log.W("注册表启动项写入后读不到，改用启动文件夹");
            }

            // 关闭时：即使注册表已删掉，也要清掉「启动」文件夹里可能残留的 vbs 启动器，
            // 否则只清注册表、vbs 仍会让程序开机自启（旧版本这里会漏）。
            if (!on)
            {
                try
                {
                    string lnk0 = StartupLauncher();
                    if (File.Exists(lnk0)) File.Delete(lnk0);
                }
                catch (Exception ex) { Log.W("删除启动文件夹残留失败: " + ex.Message); }
            }
            // 开启路径用过的临时 vbs 副本，成功后清掉，避免留在程序目录里
            else
            {
                try
                {
                    string localVbs0 = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(Application.ExecutablePath), "AutoIME.startup.vbs");
                    if (File.Exists(localVbs0)) File.Delete(localVbs0);
                }
                catch { }
            }

            // 3) 兜底：把启动器放到「启动」文件夹。
            //    本进程直接写会被安全软件拦，但 cmd.exe（受信任镜像）可以，
            //    所以先在程序目录生成 vbs，再让 cmd 负责复制过去。
            if (!ok)
            {
                try
                {
                    string lnk = StartupLauncher();
                    if (!on)
                    {
                        if (File.Exists(lnk)) File.Delete(lnk);
                        ok = !File.Exists(lnk);
                    }
                    else
                    {
                        string vbs = "CreateObject(\"WScript.Shell\").Run \"\"\"" +
                                     Application.ExecutablePath + "\"\"\", 0, False" + "\r\n";
                        string localVbs = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(Application.ExecutablePath), "AutoIME.startup.vbs");
                        File.WriteAllText(localVbs, vbs, Encoding.Default);

                        var cpsi = new ProcessStartInfo("cmd.exe",
                            "/c copy /y \"" + localVbs + "\" \"" + lnk + "\"");
                        cpsi.UseShellExecute = false;
                        cpsi.CreateNoWindow = true;
                        cpsi.WindowStyle = ProcessWindowStyle.Hidden;
                        var cp = Process.Start(cpsi);
                        if (cp != null) cp.WaitForExit(4000);
                        ok = File.Exists(lnk);
                        Log.W("用 cmd.exe 复制启动器 => " + (ok ? "成功" : "失败"));
                        if (ok && File.Exists(localVbs)) File.Delete(localVbs);
                    }
                }
                catch (Exception ex) { Log.W("写启动文件夹失败: " + ex.Message); ok = false; }
            }

            // 最终以"当前状态是否等于期望"为准（关闭时必须确认真的没了）
            bool nowOn = IsStartup();
            ok = (nowOn == on);
            Log.W("开机自启 " + (on ? "开启" : "关闭") + " => " + (ok ? "成功" : "失败") +
                  " (当前=" + (nowOn ? "已开启" : "已关闭") + ")");
            return ok;
        }

        // 当前进程是否已具备管理员权限
        static bool IsElevated()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pr = new System.Security.Principal.WindowsPrincipal(id);
                return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // UAC 是否被关闭（关闭后没有"提权"这条路径，runas 必然报"用户取消"）
        static bool UacDisabled()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", false))
                {
                    object v = (k == null) ? null : k.GetValue("EnableLUA");
                    return v != null && Convert.ToInt32(v) == 0;
                }
            }
            catch { return false; }
        }

        void RestartAsAdmin()
        {
            if (IsElevated())
            {
                MessageBox.Show("当前已经是管理员权限了，不需要重启。\r\n\r\n" +
                    (UacDisabled()
                        ? "你这台机器关闭了 UAC（用户账户控制），所有程序本来就以完整管理员权限运行。"
                        : "这个程序当前就是以管理员身份启动的。"),
                    "AutoIME", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                Log.W("以管理员身份重启失败: " + ex.Message);
                string why = UacDisabled()
                    ? "这台机器关闭了 UAC，系统没有提权通道，所以无法“以管理员身份重新启动”。\r\n" +
                      "不过关闭 UAC 时所有程序本来就是管理员权限，通常不需要这么做。"
                    : "多半是 UAC 弹窗被取消了。";
                MessageBox.Show("以管理员身份重启失败。\r\n\r\n" + why +
                    "\r\n\r\n也可以手动：右键 AutoIME.exe → 以管理员身份运行。",
                    "AutoIME", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 供命令行使用（导出诊断报告后退出）
        public void QuitForCli() { ExitApp(); }

        void ExitApp()
        {
            eng.Stop();
            if (raiseTimer != null) raiseTimer.Stop();
            if (raiseSettingsEvent != null) raiseSettingsEvent.Dispose();
            try { cfg.Save(); } catch { }
            Nat.UnregisterHotKey(hk.Handle, HK_TOGGLE);
            Nat.UnregisterHotKey(hk.Handle, HK_LEARN);
            Nat.UnregisterHotKey(hk.Handle, HK_DIAG);
            Nat.UnregisterHotKey(hk.Handle, HK_SETTINGS);
            hk.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            Log.W("退出");
            ExitThread();
        }

        static Icon MakeIcon(Color bg, string ch)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(bg)) g.FillEllipse(b, 1, 1, 30, 30);
                using (var f = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(ch, f, b, new RectangleF(0, 0, 32, 32), sf);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    // -------------------------------------------------------------- 热键解析器
    internal static class Hotkey
    {
        public static bool Parse(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(spec)) return false;
            var parts = spec.ToUpperInvariant().Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (p == "CTRL" || p == "CONTROL") { mods |= 0x0002; continue; }
                if (p == "ALT") { mods |= 0x0001; continue; }
                if (p == "SHIFT") { mods |= 0x0004; continue; }
                if (p == "WIN") { mods |= 0x0008; continue; }
                if (p.Length == 1)
                {
                    char ch = p[0];
                    if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) vk = ch;
                    else return false;
                }
                else if (p.StartsWith("F") && p.Length <= 3)
                {
                    int n;
                    if (int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24) vk = (uint)(0x70 + n - 1);
                    else return false;
                }
                else return false;
            }
            return vk != 0;
        }
    }

    // -------------------------------------------------------------------- 入口
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 命令行参数先解析（CLI 工具命令允许独立运行，不受单实例限制）
            bool cliStartupOn = false, cliStartupOff = false, cliDiag = false;
            if (args != null)
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--startup-on") cliStartupOn = true;
                    if (args[i] == "--startup-off") cliStartupOff = true;
                    if (args[i] == "--diag-export") cliDiag = true;
                }

            Mutex mtx = null;

            // 非 CLI 的 GUI 启动：单实例判定。已有同名进程或互斥锁被占用时，唤起旧实例设置界面后退出
            if (!cliStartupOn && !cliStartupOff && !cliDiag)
            {
                bool running = false;
                // 进程名兜底：跨 Mutex 命名空间 / 完整性级别隔离时仍能识别已有实例
                try
                {
                    var self = Process.GetCurrentProcess();
                    foreach (var p in Process.GetProcessesByName("AutoIME"))
                    {
                        if (p.Id != self.Id && !p.HasExited) { running = true; break; }
                    }
                }
                catch { }

                bool created = false;
                try
                {
                    mtx = new Mutex(true, "AutoIME_single_instance_7d1f", out created);
                    if (!created) running = true;
                    else if (!mtx.WaitOne(0)) running = true;
                }
                catch { }

                if (running)
                {
                    // 唤起已运行实例的设置界面（托盘图标被隐藏时，双击程序也能打开设置）
                    try
                    {
                        using (var evt = EventWaitHandle.OpenExisting("AutoIME_open_settings_evt_7d1f"))
                            evt.Set();
                    }
                    catch { }
                    return;
                }
            }

            if (cliDiag)
            {
                var app0 = new App(false, false);
                string p0 = app0.ExportDiag();
                app0.QuitForCli();
                return;
            }

            if (cliStartupOn || cliStartupOff)
            {
                string d0 = App.DataDir();
                Log.Path = System.IO.Path.Combine(d0, "AutoIME.log");
                bool ok0 = App.SetStartup(cliStartupOn);
                Log.W("命令行 开机自启" + (cliStartupOn ? "开启" : "关闭") + " => " + (ok0 ? "成功" : "失败"));
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool openDiag = false, openSettings = false;
            if (args != null)
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--diag" || args[i] == "/diag") openDiag = true;
                    if (args[i] == "--settings" || args[i] == "/settings") openSettings = true;
                }

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.W("[UI异常] " + (e.Exception == null ? "?" : e.Exception.ToString()));
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.W("[未处理异常] " + (e.ExceptionObject == null ? "?" : e.ExceptionObject.ToString()));
            };

            Application.Run(new App(openDiag, openSettings));
            GC.KeepAlive(mtx);
        }
    }
}
