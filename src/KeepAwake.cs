using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using KeepAwake.Shared;
using Microsoft.Win32;

[assembly: AssemblyTitle("ScreenStay")]
[assembly: AssemblyDescription("托盘工具：播放音视频时自动保持屏幕常亮，自动切换日间/夜间主题，可查看阻止休眠的程序")]
[assembly: AssemblyProduct("ScreenStay")]
[assembly: AssemblyVersion("1.5.3.0")]
[assembly: AssemblyFileVersion("1.5.3.0")]
[assembly: AssemblyInformationalVersion("1.5.3")]
[assembly: ComVisible(false)]

namespace KeepAwake
{
    /// <summary>程序入口：托盘常驻、单实例、自检模式。</summary>
    internal static class Program
    {
        internal const string MutexName = "Local\\ScreenStay_9F3C5A71";
        internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string RunValueName = "ScreenStay";
        internal const string SettingsKeyPath = @"Software\ScreenStay";
        internal const string LegacySettingsKeyPath = @"Software\KeepAwake";
        internal const string StartupArgument = "--startup";
        internal const string ViewerFolderName = "ScreenStay.Viewer";
        internal const string ViewerExeName = "ScreenStay.Viewer.exe";

        [STAThread]
        private static int Main(string[] args)
        {
            string first = args != null && args.Length > 0 ? args[0].TrimStart('-', '/') : string.Empty;

            if (string.Equals(first, "dumprequests", StringComparison.OrdinalIgnoreCase))
            {
                return DumpPowerRequests(args);
            }

            if (string.Equals(first, "viewrequests", StringComparison.OrdinalIgnoreCase))
            {
                return ViewRequests(args);
            }

            if (string.Equals(first, "selftest", StringComparison.OrdinalIgnoreCase))
            {
                return SelfTest.Run(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 从旧版 KeepAwake 迁移设置（只复制一次）
            Settings.MigrateLegacySettings();

            if (string.Equals(first, "help", StringComparison.OrdinalIgnoreCase) || first == "?")
            {
                MessageBox.Show(TrayApplication.AboutText(), "ScreenStay 使用说明",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            bool silentStart = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], StartupArgument, StringComparison.OrdinalIgnoreCase))
                    {
                        silentStart = true;
                        break;
                    }
                }
            }

            bool createdNew;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已经在运行：直接打开主界面（与托盘双击行为一致）
                    if (!TryLaunchViewerMain())
                    {
                        MessageBox.Show(
                            "ScreenStay 已经在运行了。\r\n\r\n" +
                            "可以双击任务栏右下角的 ScreenStay 托盘图标打开主界面；" +
                            "如果看不到图标，请点击任务栏上的「^」展开托盘区。",
                            "ScreenStay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return 0;
                }

                try
                {
                    using (TrayApplication app = new TrayApplication(silentStart))
                    {
                        Application.Run(app);
                    }
                }
                finally
                {
                    try { mutex.ReleaseMutex(); }
                    catch { }
                }
            }
            return 0;
        }

        /// <summary>
        /// 以管理员身份运行的辅助模式：收集 powercfg /requests 输出并写入指定文件。
        /// 主实例通过 UAC 启动本模式来获取“哪些程序在阻止休眠”。
        /// </summary>
        private static int DumpPowerRequests(string[] args)
        {
            string path = args != null && args.Length > 1
                ? args[1]
                : Path.Combine(Path.GetTempPath(), "KeepAwake-powerrequests.txt");

            StringBuilder report = new StringBuilder();
            int exitCode = -1;
            string stdout = string.Empty;
            string stderr = string.Empty;
            try
            {
                string powercfg = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe");
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = powercfg;
                psi.Arguments = "/requests";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                try
                {
                    int oemCodePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                    psi.StandardOutputEncoding = Encoding.GetEncoding(oemCodePage);
                    psi.StandardErrorEncoding = Encoding.GetEncoding(oemCodePage);
                }
                catch
                {
                }

                using (Process process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        stdout = process.StandardOutput.ReadToEnd();
                        stderr = process.StandardError.ReadToEnd();
                        if (!process.WaitForExit(20000))
                        {
                            try { process.Kill(); }
                            catch { }
                        }
                        else
                        {
                            exitCode = process.ExitCode;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
            }

            report.AppendLine("powercfg /requests");
            report.AppendLine("exit code: " + exitCode);
            report.AppendLine();
            if (!string.IsNullOrEmpty(stdout)) report.AppendLine(stdout.TrimEnd());
            if (!string.IsNullOrEmpty(stderr))
            {
                report.AppendLine("---- 错误输出 ----");
                report.AppendLine(stderr.TrimEnd());
            }

            try
            {
                File.WriteAllText(path, report.ToString(), new UTF8Encoding(true));
            }
            catch
            {
            }
            return 0;
        }

        /// <summary>查找 WinUI 3 查看器（ScreenStay.Viewer）。找不到时返回 null。</summary>
        internal static string FindViewerPath()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            if (string.IsNullOrEmpty(dir)) return null;

            string[] candidates = new string[]
            {
                Path.Combine(dir, ViewerFolderName, ViewerExeName),
                Path.Combine(dir, ViewerExeName)
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (File.Exists(candidates[i])) return candidates[i];
                }
                catch
                {
                }
            }
            return null;
        }

        /// <summary>
        /// 用 WinUI 3 查看器打开电源请求报告。成功启动返回 true；
        /// 找不到查看器或它立即异常退出（例如缺少 Windows App Runtime）时返回 false。
        /// </summary>
        internal static bool TryLaunchViewer(string reportPath)
        {
            string viewer = FindViewerPath();
            if (viewer == null) return false;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = viewer;
                psi.Arguments = "--requests \"" + reportPath + "\"";
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(viewer);

                using (Process process = Process.Start(psi))
                {
                    if (process == null) return false;

                    // 给 WinUI 一点启动时间：若立即以非 0 退出则回退到内置窗口
                    for (int i = 0; i < 15; i++)
                    {
                        if (process.HasExited) return process.ExitCode == 0;
                        System.Threading.Thread.Sleep(100);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>打开 WinUI 3 主界面（已打开则激活）。成功返回 true。</summary>
        internal static bool TryLaunchViewerMain()
        {
            if (TryActivateExistingMainWindow()) return true;

            string viewer = FindViewerPath();
            if (viewer == null) return false;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = viewer;
                psi.Arguments = "--main";
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetDirectoryName(viewer);
                using (Process process = Process.Start(psi))
                {
                    return process != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>如果主界面已经打开，就把它激活到前台。</summary>
        internal static bool TryActivateExistingMainWindow()
        {
            try
            {
                IntPtr hwnd = FindWindow(null, "ScreenStay 主界面");
                if (hwnd == IntPtr.Zero) return false;
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>隐藏模式：显示指定的电源请求报告文件（用于调试 / 脚本调用）。</summary>
        private static int ViewRequests(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string path = args != null && args.Length > 1
                ? args[1]
                : Path.Combine(Path.GetTempPath(), "KeepAwake-powerrequests.txt");

            if (TryLaunchViewer(path))
            {
                return 0;
            }

            string content;
            try
            {
                content = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                content = "读取报告失败：" + ex.Message;
            }
            RequestsViewer.Show(content);
            return 0;
        }
    }

    /// <summary>“正在阻止屏幕休眠的程序”查看窗口。</summary>
    internal static class RequestsViewer
    {
        private static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text.Replace("\n", "\r\n");
        }

        internal static void Show(string content)
        {
            string header =
                "以下是 Windows 当前的系统电源请求（powercfg /requests）。\r\n" +
                "DISPLAY / 显示类请求：相关程序正在阻止屏幕关闭；\r\n" +
                "SYSTEM / 系统类请求：相关程序正在阻止系统睡眠。\r\n" +
                "进程路径以 [PROCESS] / [进程] 开头；数据需要管理员权限才能读取。\r\n\r\n";

            Font monoFont;
            try
            {
                monoFont = new Font("Consolas", 9f);
            }
            catch
            {
                monoFont = new Font(FontFamily.GenericMonospace, 9f);
            }

            using (Form form = new Form())
            {
                form.Text = "ScreenStay - 正在阻止屏幕休眠的程序";
                form.StartPosition = FormStartPosition.CenterScreen;
                form.ClientSize = new Size(820, 560);
                form.MinimizeBox = false;
                form.ShowIcon = false;

                TextBox text = new TextBox();
                text.Multiline = true;
                text.ReadOnly = true;
                text.ScrollBars = ScrollBars.Both;
                text.WordWrap = false;
                text.Font = monoFont;
                text.Bounds = new Rectangle(12, 12, 796, 484);
                text.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                text.Text = header + NormalizeLineEndings(content);
                text.Select(0, 0);

                Button copyButton = new Button();
                copyButton.Text = "复制到剪贴板";
                copyButton.Bounds = new Rectangle(12, 508, 140, 34);
                copyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                copyButton.Click += delegate
                {
                    try { Clipboard.SetText(text.Text); }
                    catch { }
                };

                Button closeButton = new Button();
                closeButton.Text = "关闭";
                closeButton.Bounds = new Rectangle(form.ClientSize.Width - 122, 508, 110, 34);
                closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                closeButton.DialogResult = DialogResult.OK;

                form.Controls.Add(text);
                form.Controls.Add(copyButton);
                form.Controls.Add(closeButton);
                form.AcceptButton = closeButton;
                form.CancelButton = closeButton;
                form.ShowDialog();
            }

            monoFont.Dispose();
        }
    }

    /// <summary>Win32 电源相关调用。</summary>
    internal static class Native
    {
        internal const uint ES_CONTINUOUS = 0x80000000;
        internal const uint ES_SYSTEM_REQUIRED = 0x00000001;
        internal const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private const uint WM_SYSCOMMAND = 0x0112;
        private const int SC_MONITORPOWER = 0xF170;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam,
            IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        /// <summary>阻止屏幕休眠与系统空闲睡眠。必须在一直存活的线程上调用。</summary>
        internal static bool KeepAwake()
        {
            return SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED) != 0;
        }

        /// <summary>撤销唤醒请求，恢复系统默认的熄屏 / 睡眠行为。</summary>
        internal static void ReleaseAwake()
        {
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        /// <summary>如果屏幕当前处于关闭状态，把它点亮。</summary>
        internal static void TurnOnDisplay()
        {
            IntPtr result;
            SendMessageTimeout(new IntPtr(0xFFFF), WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER),
                new IntPtr(-1), SMTO_ABORTIFHUNG, 1000, out result);
        }
    }

    /// <summary>Windows 浅色 / 深色主题切换（应用 + 系统）。</summary>
    internal static class Theme
    {
        private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint WM_THEMECHANGED = 0x031A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam,
            string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam,
            IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        /// <summary>当前是否为深色主题（读取 AppsUseLightTheme）。</summary>
        internal static bool IsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue("AppsUseLightTheme");
                    if (value == null) return false;
                    return Convert.ToInt32(value) == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>切换应用主题 + 系统主题，并广播通知让已打开的程序刷新配色。</summary>
        internal static void Apply(bool dark)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
                        key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }

            try
            {
                IntPtr result;
                SendMessageTimeout(new IntPtr(0xFFFF), WM_SETTINGCHANGE, IntPtr.Zero,
                    "ImmersiveColorSet", SMTO_ABORTIFHUNG, 1000, out result);
                SendMessageTimeout(new IntPtr(0xFFFF), WM_THEMECHANGED, IntPtr.Zero,
                    IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out result);
            }
            catch
            {
            }
        }
    }

    /// <summary>日间 / 夜间主题的时间计算。</summary>
    internal static class ThemeScheduler
    {
        internal static readonly TimeSpan DefaultDayStart = new TimeSpan(7, 0, 0);
        internal static readonly TimeSpan DefaultNightStart = new TimeSpan(19, 0, 0);

        /// <summary>判断当前时间是否应使用深色主题。</summary>
        internal static bool IsDarkNow(TimeSpan dayStart, TimeSpan nightStart, TimeSpan now)
        {
            bool inDay;
            if (dayStart <= nightStart)
            {
                inDay = now >= dayStart && now < nightStart;
            }
            else
            {
                // 跨午夜：日间时间段绕过 00:00
                inDay = now >= dayStart || now < nightStart;
            }
            return !inDay;
        }

        internal static bool TryParseTime(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Trim().Split(':');
            if (parts.Length < 2) return false;
            int hour;
            int minute;
            if (!int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute)) return false;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return false;
            value = new TimeSpan(hour, minute, 0);
            return true;
        }

        internal static string FormatTime(TimeSpan value)
        {
            return value.Hours.ToString("00") + ":" + value.Minutes.ToString("00");
        }
    }

    /// <summary>
    /// 通过 Windows 音频会话（WASAPI）判断当前是否有音乐 / 视频正在播放。
    /// 只要默认音频设备上存在处于 Active 状态的音频会话，就视为“正在播放”。
    /// 静音播放同样算播放（音频会话状态仍为 Active），因此静音看视频也会保持常亮。
    /// </summary>
    internal static class MediaDetector
    {
        private enum AudioSessionState
        {
            Inactive = 0,
            Active = 1,
            Expired = 2
        }

        private const int EDataFlowRender = 0;
        private const int ERoleMultimedia = 1;
        private const int CLSCTXAll = 23; // CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr propertyStore);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        // 注意：IAudioSessionManager2 的所有方法（含基接口 IAudioSessionManager 的两个方法）
        // 必须按 vtable 顺序完整声明，不能用 C# 接口继承，否则方法槽位错位，
        // GetSessionEnumerator 会返回 E_POINTER (0x80004003)。
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(ref Guid audioSessionGuid, int streamFlags, out IntPtr sessionControl);
            [PreserveSig] int GetSimpleAudioVolume(ref Guid audioSessionGuid, int streamFlags, out IntPtr audioVolume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
            [PreserveSig] int RegisterSessionNotification(IntPtr sessionNotification);
            [PreserveSig] int UnregisterSessionNotification(IntPtr sessionNotification);
            [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
            [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int sessionCount);
            [PreserveSig] int GetSession(int sessionIndex, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            [PreserveSig] int GetState(out AudioSessionState state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingId);
            [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        }

        // 同样需要按 vtable 顺序完整声明基接口 IAudioSessionControl 的方法，再追加 IAudioSessionControl2 的方法
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out AudioSessionState state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
            [PreserveSig] int GetGroupingParam(out Guid groupingId);
            [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string instanceId);
            [PreserveSig] int GetProcessId(out uint processId);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference(bool optOut);
        }

        /// <summary>当前是否有音频会话处于播放状态。失败或无音频设备时返回 false。</summary>
        internal static bool IsMediaPlaying()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 manager = null;
            IAudioSessionEnumerator sessions = null;
            object rawManager = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out device) != 0 || device == null)
                {
                    return false;
                }

                Guid iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, CLSCTXAll, IntPtr.Zero, out rawManager) != 0 || rawManager == null)
                {
                    return false;
                }
                manager = (IAudioSessionManager2)rawManager;
                rawManager = null;

                if (manager.GetSessionEnumerator(out sessions) != 0 || sessions == null)
                {
                    return false;
                }

                int count;
                if (sessions.GetCount(out count) != 0)
                {
                    return false;
                }

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl control;
                    if (sessions.GetSession(i, out control) != 0 || control == null)
                    {
                        continue;
                    }
                    try
                    {
                        AudioSessionState state;
                        if (control.GetState(out state) == 0 && state == AudioSessionState.Active)
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(control);
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (rawManager != null)
                {
                    try { Marshal.ReleaseComObject(rawManager); }
                    catch { }
                }
                if (sessions != null)
                {
                    try { Marshal.ReleaseComObject(sessions); }
                    catch { }
                }
                if (manager != null)
                {
                    try { Marshal.ReleaseComObject(manager); }
                    catch { }
                }
                if (device != null)
                {
                    try { Marshal.ReleaseComObject(device); }
                    catch { }
                }
                if (enumerator != null)
                {
                    try { Marshal.ReleaseComObject(enumerator); }
                    catch { }
                }
            }
        }

        /// <summary>当前正在播放音频的进程名（去重，最多 8 个）。</summary>
        internal static List<string> GetPlayingProcessNames()
        {
            List<string> names = new List<string>();
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 manager = null;
            IAudioSessionEnumerator sessions = null;
            object rawManager = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out device) != 0 || device == null)
                {
                    return names;
                }

                Guid iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, CLSCTXAll, IntPtr.Zero, out rawManager) != 0 || rawManager == null)
                {
                    return names;
                }
                manager = (IAudioSessionManager2)rawManager;
                rawManager = null;

                if (manager.GetSessionEnumerator(out sessions) != 0 || sessions == null)
                {
                    return names;
                }

                int count;
                if (sessions.GetCount(out count) != 0) return names;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl control;
                    if (sessions.GetSession(i, out control) != 0 || control == null)
                    {
                        continue;
                    }
                    try
                    {
                        AudioSessionState state;
                        if (control.GetState(out state) != 0 || state != AudioSessionState.Active)
                        {
                            continue;
                        }

                        IAudioSessionControl2 control2 = control as IAudioSessionControl2;
                        if (control2 == null) continue;
                        if (control2.IsSystemSoundsSession() == 0) continue; // 系统提示音不算播放软件
                        uint processId;
                        if (control2.GetProcessId(out processId) != 0 || processId == 0) continue;

                        string processName = null;
                        try
                        {
                            using (Process process = Process.GetProcessById((int)processId))
                            {
                                processName = process.ProcessName;
                            }
                        }
                        catch
                        {
                        }
                        if (string.IsNullOrEmpty(processName)) continue;
                        if (string.Equals(processName, "Idle", StringComparison.OrdinalIgnoreCase)) continue;

                        bool exists = false;
                        for (int j = 0; j < names.Count; j++)
                        {
                            if (string.Equals(names[j], processName, StringComparison.OrdinalIgnoreCase))
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists) names.Add(processName);
                        if (names.Count >= 8) break;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(control);
                    }
                }
                return names;
            }
            catch
            {
                return names;
            }
            finally
            {
                if (rawManager != null)
                {
                    try { Marshal.ReleaseComObject(rawManager); }
                    catch { }
                }
                if (sessions != null)
                {
                    try { Marshal.ReleaseComObject(sessions); }
                    catch { }
                }
                if (manager != null)
                {
                    try { Marshal.ReleaseComObject(manager); }
                    catch { }
                }
                if (device != null)
                {
                    try { Marshal.ReleaseComObject(device); }
                    catch { }
                }
                if (enumerator != null)
                {
                    try { Marshal.ReleaseComObject(enumerator); }
                    catch { }
                }
            }
        }
    }

    /// <summary>设置与开机启动项（都在 HKEY_CURRENT_USER 下，无需管理员权限）。</summary>
    internal static class Settings
    {
        /// <summary>把旧版 KeepAwake 的设置复制到 ScreenStay（仅首次）。</summary>
        internal static void MigrateLegacySettings()
        {
            try
            {
                using (RegistryKey legacy = Registry.CurrentUser.OpenSubKey(Program.LegacySettingsKeyPath, false))
                {
                    if (legacy == null) return;
                    using (RegistryKey target = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                    {
                        if (target == null) return;
                        if (target.GetValue("Awake") != null) return; // 已经迁移过

                        string[] names = new string[]
                        {
                            "Awake", "Notifications", "AutoTheme", "ThemeMode",
                            "ThemeDayStart", "ThemeNightStart", "ThemeLat", "ThemeLon", "ThemeCity"
                        };
                        for (int i = 0; i < names.Length; i++)
                        {
                            object value = legacy.GetValue(names[i]);
                            if (value == null) continue;
                            RegistryValueKind kind = legacy.GetValueKind(names[i]);
                            target.SetValue(names[i], value, kind);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        internal static bool LoadAwake()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key == null) return true; // 首次运行默认开启
                    object value = key.GetValue("Awake");
                    if (value == null) return true;
                    return Convert.ToInt32(value) != 0;
                }
            }
            catch
            {
                return true;
            }
        }

        internal static void SaveAwake(bool awake)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("Awake", awake ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }
        }

        internal static bool LoadNotifications()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key == null) return true;
                    object value = key.GetValue("Notifications");
                    if (value == null) return true;
                    return Convert.ToInt32(value) != 0;
                }
            }
            catch
            {
                return true;
            }
        }

        internal static void SaveNotifications(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("Notifications", enabled ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }
        }

        internal static bool LoadAutoTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue("AutoTheme");
                    if (value == null) return false;
                    return Convert.ToInt32(value) != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void SaveAutoTheme(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("AutoTheme", enabled ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }
        }

        internal static string LoadThemeDayStart()
        {
            return LoadThemeTime("ThemeDayStart", ThemeScheduler.DefaultDayStart);
        }

        internal static string LoadThemeNightStart()
        {
            return LoadThemeTime("ThemeNightStart", ThemeScheduler.DefaultNightStart);
        }

        private static string LoadThemeTime(string valueName, TimeSpan fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        string text = key.GetValue(valueName) as string;
                        TimeSpan parsed;
                        if (!string.IsNullOrEmpty(text) && ThemeScheduler.TryParseTime(text, out parsed))
                        {
                            return ThemeScheduler.FormatTime(parsed);
                        }
                    }
                }
            }
            catch
            {
            }
            return ThemeScheduler.FormatTime(fallback);
        }

        internal static void SaveThemeTimes(string dayStart, string nightStart)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("ThemeDayStart", dayStart, RegistryValueKind.String);
                        key.SetValue("ThemeNightStart", nightStart, RegistryValueKind.String);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>主题切换模式：fixed（自定义时间）/ sunrise（自动定位）/ city（手动城市）/ nightlight（跟随夜间模式）。</summary>
        internal static string LoadThemeMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        string mode = key.GetValue("ThemeMode") as string;
                        if (!string.IsNullOrEmpty(mode)) return mode.Trim().ToLowerInvariant();
                    }
                }
            }
            catch
            {
            }
            return "fixed";
        }

        internal static void SaveThemeMode(string mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null) key.SetValue("ThemeMode", mode, RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }

        internal static bool LoadThemeLocation(out double latitude, out double longitude)
        {
            latitude = 0.0;
            longitude = 0.0;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key == null) return false;
                    string latText = key.GetValue("ThemeLat") as string;
                    string lonText = key.GetValue("ThemeLon") as string;
                    if (string.IsNullOrEmpty(latText) || string.IsNullOrEmpty(lonText)) return false;
                    if (!double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)) return false;
                    if (!double.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)) return false;
                    if (latitude < -90.0 || latitude > 90.0 || longitude < -180.0 || longitude > 180.0) return false;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void SaveThemeLocation(double latitude, double longitude)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("ThemeLat", latitude.ToString("0.000000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                        key.SetValue("ThemeLon", longitude.ToString("0.000000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    }
                }
            }
            catch
            {
            }
        }

        internal static void ClearThemeLocation()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("ThemeLat", false);
                        key.DeleteValue("ThemeLon", false);
                    }
                }
            }
            catch
            {
            }
        }

        internal static string LoadThemeCity()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        string city = key.GetValue("ThemeCity") as string;
                        if (!string.IsNullOrEmpty(city)) return city;
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        internal static void SaveThemeCity(string city)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null)
                    {
                        if (string.IsNullOrEmpty(city)) key.DeleteValue("ThemeCity", false);
                        else key.SetValue("ThemeCity", city, RegistryValueKind.String);
                    }
                }
            }
            catch
            {
            }
        }

        internal static void SaveCorePath(string path)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null) key.SetValue("CorePath", path, RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }

        internal static string LoadCorePath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        string path = key.GetValue("CorePath") as string;
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>当前监测状态（供主界面读取显示）。</summary>
        internal static void SaveStatus(string status)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null) key.SetValue("StatusText", status, RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }

        internal static string LoadStatus()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        string status = key.GetValue("StatusText") as string;
                        if (!string.IsNullOrEmpty(status)) return status;
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        /// <summary>当前正在播放音频的软件名（以「、」分隔，供主界面显示）。</summary>
        internal static void SavePlayingApps(string apps)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKeyPath))
                {
                    if (key != null)
                    {
                        if (string.IsNullOrEmpty(apps)) key.DeleteValue("PlayingApps", false);
                        else key.SetValue("PlayingApps", apps, RegistryValueKind.String);
                    }
                }
            }
            catch
            {
            }
        }

        internal static string LoadPlayingApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        string apps = key.GetValue("PlayingApps") as string;
                        if (!string.IsNullOrEmpty(apps)) return apps;
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        /// <summary>系统当前是否使用浅色主题（用于托盘菜单配色）。</summary>
        internal static bool IsSystemLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("SystemUsesLightTheme");
                        if (value != null) return Convert.ToInt32(value) != 0;
                    }
                }
            }
            catch
            {
            }
            return true;
        }

        internal static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.RunKeyPath, false))
                {
                    if (key == null) return false;
                    string value = key.GetValue(Program.RunValueName) as string;
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>启动项是否指向当前这个 exe。</summary>
        internal static bool IsAutoStartCurrent()
        {
            try
            {
                string value;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.RunKeyPath, false))
                {
                    value = key == null ? null : key.GetValue(Program.RunValueName) as string;
                }
                if (string.IsNullOrEmpty(value)) return false;

                string path = value.Trim();
                if (path.StartsWith("\"", StringComparison.Ordinal))
                {
                    int end = path.IndexOf('"', 1);
                    path = end > 0 ? path.Substring(1, end - 1) : path.Trim('"');
                }
                return string.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static void SetAutoStart(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.RunKeyPath))
            {
                if (key == null) throw new InvalidOperationException("无法打开注册表启动项。");
                if (enabled)
                {
                    key.SetValue(Program.RunValueName,
                        "\"" + Application.ExecutablePath + "\" " + Program.StartupArgument,
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(Program.RunValueName, false);
                }
            }
        }
    }

    /// <summary>托盘程序主体：监测音视频播放，播放时保持屏幕常亮，停止后交还电源管理。</summary>
    internal sealed class TrayApplication : ApplicationContext
    {
        /// <summary>播放停止后再多保持一会儿，避免切歌 / 缓冲间隙反复切换电源请求。</summary>
        private const int MediaHoldSeconds = 10;

        private readonly List<MemoryStream> _iconStreams = new List<MemoryStream>();
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _modeItem;
        private readonly Timer _monitorTimer;
        private readonly Timer _clickTimer;
        private readonly Icon _iconOn;
        private readonly Icon _iconIdle;
        private readonly Icon _iconOff;

        private bool _enabled;              // 用户是否开启了智能保持常亮
        private bool _playing;              // 上一次检测到是否有音视频在播放
        private bool _keepingAwake;         // 当前是否正持有“阻止熄屏”请求
        private DateTime _lastMediaUtc = DateTime.MinValue;
        private string _playingApps = string.Empty; // 当前播放软件名（以「、」分隔，供主界面显示）

        private bool _autoTheme;            // 是否开启了自动切换日间/夜间主题
        private DateTime _lastThemeCheckUtc = DateTime.MinValue;
        private string _themeSignature;
        private bool _hasThemeState;
        private bool _lastScheduledDark;

        private bool _exited;

        internal TrayApplication(bool silentStart)
        {
            Icon on = LoadIconResource("KeepAwake.Resources.icon-on.ico");
            Icon idle = LoadIconResource("KeepAwake.Resources.icon-monitor.ico");
            Icon off = LoadIconResource("KeepAwake.Resources.icon-off.ico");
            _iconOn = on != null ? on : SystemIcons.Application;
            _iconIdle = idle != null ? idle : _iconOn;
            _iconOff = off != null ? off : SystemIcons.Application;

            _statusItem = new ToolStripMenuItem("自动防息屏未开启");
            _statusItem.Enabled = false;

            _modeItem = new ToolStripMenuItem("自动防息屏（播放音视频时保持常亮）");
            _modeItem.Click += OnModeItemClick;

            ToolStripMenuItem openMainItem = new ToolStripMenuItem("打开主页面");
            openMainItem.Click += OnOpenMainClick;

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += OnExitClick;

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = true;
            _menu.Font = new Font("Segoe UI", 9f);
            _menu.Padding = new Padding(4, 6, 4, 6);
            _menu.Renderer = new ModernMenuRenderer(ModernMenuRenderer.GetColors());
            _menu.Opened += OnMenuOpened;
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_modeItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(openMainItem);
            _menu.Items.Add(exitItem);
            for (int i = 0; i < _menu.Items.Count; i++)
            {
                if (!(_menu.Items[i] is ToolStripSeparator))
                {
                    _menu.Items[i].Padding = new Padding(8, 4, 8, 4);
                }
            }

            _monitorTimer = new Timer();
            _monitorTimer.Interval = 3000;
            _monitorTimer.Tick += OnMonitorTick;

            _clickTimer = new Timer();
            _clickTimer.Interval = 280;
            _clickTimer.Tick += OnClickTimerTick;

            _notifyIcon = new NotifyIcon();
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.MouseClick += OnTrayMouseClick;
            _notifyIcon.DoubleClick += OnTrayDoubleClick;
            _notifyIcon.Icon = _iconOff;
            _notifyIcon.Text = "ScreenStay";

            _enabled = Settings.LoadAwake();
            _autoTheme = Settings.LoadAutoTheme();
            Settings.SaveCorePath(Application.ExecutablePath);
            _notifyIcon.Visible = true;

            Evaluate();
            _monitorTimer.Enabled = true;
            MaybeApplyTheme();

            if (!silentStart)
            {
                if (_enabled)
                {
                    ShowBalloon("ScreenStay 已启动",
                        "自动防息屏已开启：检测到音乐/视频播放时自动保持屏幕常亮；单击托盘图标打开主界面，双击快速开关。",
                        ToolTipIcon.Info);
                }
                else
                {
                    ShowBalloon("ScreenStay 已在托盘运行",
                        "单击托盘图标打开主界面，或右键选择「自动防息屏」开启。",
                        ToolTipIcon.Info);
                }
            }
        }

        private Icon LoadIconResource(string name)
        {
            try
            {
                Assembly assembly = typeof(TrayApplication).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    byte[] buffer = new byte[stream.Length];
                    int offset = 0;
                    while (offset < buffer.Length)
                    {
                        int read = stream.Read(buffer, offset, buffer.Length - offset);
                        if (read <= 0) break;
                        offset += read;
                    }
                    MemoryStream memory = new MemoryStream(buffer, false);
                    _iconStreams.Add(memory);
                    return new Icon(memory, SystemInformation.SmallIconSize);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>每隔几秒检查一次媒体播放状态，并同步电源请求与界面状态。</summary>
        private void Evaluate()
        {
            if (!_enabled)
            {
                if (_keepingAwake)
                {
                    Native.ReleaseAwake();
                    _keepingAwake = false;
                }
                _playing = false;
                _playingApps = string.Empty;
                UpdateUi();
                return;
            }

            bool playing = false;
            try
            {
                playing = MediaDetector.IsMediaPlaying();
            }
            catch
            {
                playing = false;
            }

            if (playing)
            {
                _lastMediaUtc = DateTime.UtcNow;
            }
            _playing = playing;

            if (playing)
            {
                List<string> appNames = MediaDetector.GetPlayingProcessNames();
                _playingApps = appNames != null && appNames.Count > 0
                    ? string.Join("、", appNames.ToArray())
                    : string.Empty;
            }
            else
            {
                _playingApps = string.Empty;
            }

            bool hold = playing || (DateTime.UtcNow - _lastMediaUtc).TotalSeconds < MediaHoldSeconds;
            if (hold)
            {
                if (!_keepingAwake)
                {
                    if (Native.KeepAwake())
                    {
                        _keepingAwake = true;
                        Native.TurnOnDisplay();
                    }
                }
                else
                {
                    Native.KeepAwake(); // 定时重新申请，避免被其它程序重置
                }
            }
            else if (_keepingAwake)
            {
                Native.ReleaseAwake();
                _keepingAwake = false;
            }

            UpdateUi();
        }

        private void UpdateUi()
        {
            _modeItem.Checked = _enabled;

            string status;
            if (!_enabled)
            {
                status = "自动防息屏未开启";
                _notifyIcon.Icon = _iconOff;
            }
            else if (_playing)
            {
                status = "已开启，检测到播放";
                _notifyIcon.Icon = _iconOn;      // 红色显示器
            }
            else if (_keepingAwake)
            {
                status = "已开启，播放暂停缓冲中";
                _notifyIcon.Icon = _iconIdle;    // 绿色显示器
            }
            else
            {
                status = "已开启，无音视频播放";
                _notifyIcon.Icon = _iconIdle;    // 绿色显示器
            }

            _statusItem.Text = status;
            _notifyIcon.Text = "ScreenStay：" + status;
            Settings.SaveStatus(status);
            Settings.SavePlayingApps(_enabled ? _playingApps : string.Empty);
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            if (!Settings.LoadNotifications()) return;
            try
            {
                _notifyIcon.ShowBalloonTip(4000, title, text, icon);
            }
            catch
            {
            }
        }

        private void OnModeItemClick(object sender, EventArgs e)
        {
            _enabled = !_enabled;
            Settings.SaveAwake(_enabled);
            _lastMediaUtc = DateTime.MinValue;
            Evaluate();

            if (_enabled)
            {
                ShowBalloon("已开启智能保持常亮",
                    "检测到音乐/视频播放时会自动保持屏幕常亮；没有播放时不会干扰 Windows 电源管理。",
                    ToolTipIcon.Info);
            }
            else
            {
                ShowBalloon("已关闭智能保持常亮",
                    "已恢复 Windows 默认的熄屏与睡眠设置。", ToolTipIcon.Info);
            }
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // 延迟打开主界面：双击时不再同时触发单击行为
            _clickTimer.Stop();
            _clickTimer.Start();
        }

        private void OnClickTimerTick(object sender, EventArgs e)
        {
            _clickTimer.Stop();
            OpenMainWindow();
        }

        private void OnTrayDoubleClick(object sender, EventArgs e)
        {
            _clickTimer.Stop();
            OnModeItemClick(null, EventArgs.Empty);
        }

        private void OnMenuOpened(object sender, EventArgs e)
        {
            ModernMenuRenderer renderer = _menu.Renderer as ModernMenuRenderer;
            if (renderer != null) renderer.SetColors(ModernMenuRenderer.GetColors());
            try
            {
                using (GraphicsPath path = ModernMenuRenderer.RoundedRect(
                    new Rectangle(0, 0, _menu.Width, _menu.Height), 8))
                {
                    _menu.Region = new Region(path);
                }
            }
            catch
            {
            }
        }

        private void OnOpenMainClick(object sender, EventArgs e)
        {
            OpenMainWindow();
        }

        /// <summary>打开 WinUI 3 主界面（已打开则激活）。</summary>
        private void OpenMainWindow()
        {
            if (Program.TryLaunchViewerMain()) return;
            MessageBox.Show(
                "未找到主界面程序（ScreenStay.Viewer）。\r\n\r\n" +
                "请保留安装目录中的 ScreenStay.Viewer 文件夹，或重新安装完整版本。",
                "ScreenStay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnMonitorTick(object sender, EventArgs e)
        {
            // 主界面里可以改开关，这里定期同步注册表状态
            bool awake = Settings.LoadAwake();
            if (awake != _enabled)
            {
                _enabled = awake;
                _lastMediaUtc = DateTime.MinValue;
            }
            Evaluate();
            MaybeApplyTheme();
        }

        /// <summary>按当前模式（自定义时间 / 日出日落 / 跟随 Windows 夜间模式）自动切换主题。</summary>
        private void MaybeApplyTheme()
        {
            bool auto = Settings.LoadAutoTheme();
            if (auto != _autoTheme)
            {
                _autoTheme = auto;
                _hasThemeState = false;
            }
            if (!_autoTheme) return;

            if ((DateTime.UtcNow - _lastThemeCheckUtc).TotalSeconds < 10) return;
            _lastThemeCheckUtc = DateTime.UtcNow;

            string mode = Settings.LoadThemeMode();
            string dayText = Settings.LoadThemeDayStart();
            string nightText = Settings.LoadThemeNightStart();
            string city = Settings.LoadThemeCity();
            double latitude;
            double longitude;
            bool hasLocation = Settings.LoadThemeLocation(out latitude, out longitude);

            bool dark = false;
            bool resolved = false;

            if (string.Equals(mode, "nightlight", StringComparison.OrdinalIgnoreCase))
            {
                bool nightLightOn;
                if (NightLightState.TryIsOn(out nightLightOn))
                {
                    dark = nightLightOn;
                    resolved = true;
                }
            }
            else if ((string.Equals(mode, "sunrise", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(mode, "city", StringComparison.OrdinalIgnoreCase)) && hasLocation)
            {
                TimeSpan sunrise;
                TimeSpan sunset;
                if (SunCalculator.TryCompute(DateTime.Now, latitude, longitude, out sunrise, out sunset))
                {
                    dark = ThemeScheduler.IsDarkNow(sunrise, sunset, DateTime.Now.TimeOfDay);
                    resolved = true;
                }
            }

            if (!resolved)
            {
                TimeSpan dayStart;
                TimeSpan nightStart;
                if (!ThemeScheduler.TryParseTime(dayText, out dayStart)) dayStart = ThemeScheduler.DefaultDayStart;
                if (!ThemeScheduler.TryParseTime(nightText, out nightStart)) nightStart = ThemeScheduler.DefaultNightStart;
                dark = ThemeScheduler.IsDarkNow(dayStart, nightStart, DateTime.Now.TimeOfDay);
            }

            string signature = mode + "|" + dayText + "|" + nightText + "|" + city + "|" +
                               latitude.ToString(CultureInfo.InvariantCulture) + "|" +
                               longitude.ToString(CultureInfo.InvariantCulture) + "|" + resolved;
            bool settingsChanged = _themeSignature != signature;
            _themeSignature = signature;

            if (settingsChanged || !_hasThemeState || dark != _lastScheduledDark)
            {
                Theme.Apply(dark);
                _hasThemeState = true;
                _lastScheduledDark = dark;
            }
        }

        /// <summary>检测是否同时运行了其它自动主题工具（避免两者互相覆盖设置）。</summary>
        /// <summary>
        /// 查看当前有哪些程序持有显示 / 系统电源请求。
        /// Windows 只允许管理员读取这些数据，因此用 UAC 以管理员身份运行本程序的
        /// --dumprequests 辅助模式，把 powercfg /requests 输出写入临时文件后再展示。
        /// </summary>
        private void OnExitClick(object sender, EventArgs e)
        {
            ExitApplication();
        }

        private void ExitApplication()
        {
            if (_exited) return;
            _exited = true;

            try { _monitorTimer.Stop(); _monitorTimer.Dispose(); }
            catch { }
            try { _clickTimer.Stop(); _clickTimer.Dispose(); }
            catch { }
            try { Native.ReleaseAwake(); }
            catch { }
            try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
            catch { }
            try { _menu.Dispose(); }
            catch { }
            for (int i = 0; i < _iconStreams.Count; i++)
            {
                try { _iconStreams[i].Dispose(); }
                catch { }
            }
            _iconStreams.Clear();

            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ExitApplication();
            base.Dispose(disposing);
        }

        internal static string AboutText()
        {
            string nl = "\r\n";
            StringBuilder sb = new StringBuilder();
            sb.Append("ScreenStay 1.5.3 —— Windows 托盘「智能保持屏幕常亮」工具").Append(nl).Append(nl);
            sb.Append("工作原理：").Append(nl);
            sb.Append("  开启后每 3 秒检测系统是否有音频正在播放（音乐 / 视频等）；").Append(nl);
            sb.Append("  检测到播放时，阻止屏幕关闭与系统空闲睡眠；").Append(nl);
            sb.Append("  没有播放时，立即把熄屏 / 睡眠交还给 Windows 电源管理。").Append(nl).Append(nl);
            sb.Append("主界面（WinUI 3）：").Append(nl);
            sb.Append("  单击托盘图标，或右键托盘图标 →「打开主页面」").Append(nl);
            sb.Append("  双击托盘图标：快速开关自动防息屏").Append(nl);
            sb.Append("  · 常规：自动防息屏开关、开机自动启动、弹窗提醒、查看阻止休眠的程序").Append(nl);
            sb.Append("  · 主题：4 种日间 / 夜间自动切换方式").Append(nl);
            sb.Append("  · 阻止休眠的程序：查看 Windows 电源请求（需要管理员权限）").Append(nl);
            sb.Append("  · 使用说明 / 关于：帮助与版本信息").Append(nl).Append(nl);
            sb.Append("托盘右键菜单：").Append(nl);
            sb.Append("  · 当前状态（置顶，只读）：自动防息屏未开启 / 已开启，无音视频播放 /").Append(nl);
            sb.Append("    已开启，检测到播放 / 已开启，播放暂停缓冲中").Append(nl);
            sb.Append("  · 自动防息屏（播放音视频时保持常亮）").Append(nl);
            sb.Append("  · 打开主页面").Append(nl);
            sb.Append("  · 退出：关闭程序并恢复系统默认电源管理").Append(nl);
            sb.Append("  · 双击托盘图标：快速开关自动防息屏").Append(nl).Append(nl);
            sb.Append("托盘图标状态（只保留显示器图标，用颜色区分）：").Append(nl);
            sb.Append("  红色显示器：已开启，检测到播放").Append(nl);
            sb.Append("  绿色显示器：已开启，无音视频播放 / 播放暂停缓冲中").Append(nl);
            sb.Append("  灰色显示器：自动防息屏未开启").Append(nl).Append(nl);
            sb.Append("配置项：HKEY_CURRENT_USER\\Software\\ScreenStay").Append(nl);
            sb.Append("自启项：HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\ScreenStay");
            sb.Append(nl).Append(nl);
            sb.Append("当前程序路径：").Append(Application.ExecutablePath).Append(nl);
            sb.Append("版本：").Append(Application.ProductVersion).Append(nl).Append(nl);
            sb.Append("提示：如果托盘区看不到图标，请点击任务栏右下角的「^」，").Append(nl);
            sb.Append("把 ScreenStay 图标拖到任务栏上固定。");
            return sb.ToString();
        }
    }

    /// <summary>WinUI 风格的托盘右键菜单配色。</summary>
    internal sealed class ModernMenuColors
    {
        internal Color Background;
        internal Color Text;
        internal Color SecondaryText;
        internal Color Hover;
        internal Color Separator;
        internal Color Check;
    }

    /// <summary>托盘右键菜单的现代化绘制：圆角、悬停胶囊、深浅色跟随系统。</summary>
    internal sealed class ModernMenuRenderer : ToolStripRenderer
    {
        private ModernMenuColors _colors;

        internal ModernMenuRenderer(ModernMenuColors colors)
        {
            _colors = colors;
        }

        internal void SetColors(ModernMenuColors colors)
        {
            _colors = colors;
        }

        internal static ModernMenuColors GetColors()
        {
            bool light = Settings.IsSystemLightTheme();
            ModernMenuColors colors = new ModernMenuColors();
            if (light)
            {
                colors.Background = Color.FromArgb(249, 249, 249);
                colors.Text = Color.FromArgb(27, 27, 27);
                colors.SecondaryText = Color.FromArgb(96, 96, 96);
                colors.Hover = Color.FromArgb(235, 235, 235);
                colors.Separator = Color.FromArgb(224, 224, 224);
                colors.Check = Color.FromArgb(0, 103, 192);
            }
            else
            {
                colors.Background = Color.FromArgb(44, 44, 44);
                colors.Text = Color.FromArgb(255, 255, 255);
                colors.SecondaryText = Color.FromArgb(160, 160, 160);
                colors.Hover = Color.FromArgb(61, 61, 61);
                colors.Separator = Color.FromArgb(68, 68, 68);
                colors.Check = Color.FromArgb(96, 205, 255);
            }
            return colors;
        }

        internal static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new SolidBrush(_colors.Background))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 圆角 Region 裁剪后不需要额外边框
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Rectangle bounds = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(bounds, 5))
            using (SolidBrush brush = new SolidBrush(_colors.Hover))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color = e.Item.Enabled ? _colors.Text : _colors.SecondaryText;
            Rectangle textRect = e.TextRectangle;
            int left = textRect.Left;
            int width = e.Item.Width - left - 10;
            if (width < 10) width = Math.Max(10, e.Item.Width - 10);
            Rectangle bounds = new Rectangle(left, 0, width, e.Item.Height);
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                    TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, color, flags);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(_colors.Separator))
            {
                e.Graphics.DrawLine(pen, new Point(12, y), new Point(e.Item.Width - 12, y));
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(_colors.Check, 1.8f))
            {
                Point p1 = new Point(r.Left + 2, r.Top + r.Height / 2);
                Point p2 = new Point(r.Left + r.Width / 2 - 1, r.Bottom - 3);
                Point p3 = new Point(r.Right - 2, r.Top + 3);
                e.Graphics.DrawLines(pen, new Point[] { p1, p2, p3 });
            }
        }
    }

    /// <summary>命令行自检：ScreenStay.exe --selftest [报告路径]。</summary>
    internal static class SelfTest
    {
        internal static int Run(string[] args)
        {
            List<string> lines = new List<string>();
            int failures = 0;

            lines.Add("ScreenStay 自检 @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            lines.Add("程序路径: " + Application.ExecutablePath);
            lines.Add("版本: " + Application.ProductVersion);
            lines.Add("");

            // 1. 电源 API
            bool keep = Native.KeepAwake();
            Check(lines, ref failures, keep, "SetThreadExecutionState 保持唤醒（返回非 0）");
            Native.ReleaseAwake();
            Native.TurnOnDisplay();
            lines.Add("        已调用恢复默认状态与点亮屏幕广播");

            // 2. 媒体检测
            bool detectorOk = true;
            bool playingNow = false;
            try
            {
                playingNow = MediaDetector.IsMediaPlaying();
            }
            catch
            {
                detectorOk = false;
            }
            Check(lines, ref failures, detectorOk,
                "音频会话检测接口可用（当前是否在播放：" + (playingNow ? "是" : "否") + "）");

            // 3. 设置项
            bool beforeAwake = Settings.LoadAwake();
            Settings.SaveAwake(true);
            bool readTrue = Settings.LoadAwake();
            Settings.SaveAwake(false);
            bool readFalse = Settings.LoadAwake();
            Settings.SaveAwake(beforeAwake);
            Check(lines, ref failures, readTrue && !readFalse, "设置项读写（HKCU\\Software\\ScreenStay）");
            lines.Add("        原始设置已恢复: Awake=" + beforeAwake);

            // 通知开关
            bool beforeNotify = Settings.LoadNotifications();
            Settings.SaveNotifications(true);
            bool notifyTrue = Settings.LoadNotifications();
            Settings.SaveNotifications(false);
            bool notifyFalse = Settings.LoadNotifications();
            Settings.SaveNotifications(beforeNotify);
            Check(lines, ref failures, notifyTrue && !notifyFalse,
                "弹窗提醒开关读写（HKCU\\Software\\ScreenStay\\Notifications）");
            lines.Add("        原始通知设置已恢复: Notifications=" + beforeNotify);

            // 自动主题设置
            bool beforeAutoTheme = Settings.LoadAutoTheme();
            Settings.SaveAutoTheme(true);
            bool autoThemeTrue = Settings.LoadAutoTheme();
            Settings.SaveAutoTheme(false);
            bool autoThemeFalse = Settings.LoadAutoTheme();
            Settings.SaveAutoTheme(beforeAutoTheme);
            Check(lines, ref failures, autoThemeTrue && !autoThemeFalse,
                "自动主题开关读写（HKCU\\Software\\ScreenStay\\AutoTheme）");

            string beforeDay = Settings.LoadThemeDayStart();
            string beforeNight = Settings.LoadThemeNightStart();
            Settings.SaveThemeTimes("06:30", "20:15");
            bool timesOk = Settings.LoadThemeDayStart() == "06:30" && Settings.LoadThemeNightStart() == "20:15";
            Settings.SaveThemeTimes(beforeDay, beforeNight);
            Check(lines, ref failures, timesOk, "主题切换时间读写（ThemeDayStart / ThemeNightStart）");
            lines.Add("        原始主题时间已恢复: " + beforeDay + " / " + beforeNight);

            bool scheduleOk =
                !ThemeScheduler.IsDarkNow(new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), new TimeSpan(12, 0, 0)) &&
                !ThemeScheduler.IsDarkNow(new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), new TimeSpan(7, 0, 0)) &&
                ThemeScheduler.IsDarkNow(new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), new TimeSpan(19, 0, 0)) &&
                ThemeScheduler.IsDarkNow(new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), new TimeSpan(22, 0, 0)) &&
                ThemeScheduler.IsDarkNow(new TimeSpan(7, 0, 0), new TimeSpan(19, 0, 0), new TimeSpan(6, 59, 0)) &&
                !ThemeScheduler.IsDarkNow(new TimeSpan(19, 0, 0), new TimeSpan(7, 0, 0), new TimeSpan(23, 0, 0)) &&
                !ThemeScheduler.IsDarkNow(new TimeSpan(19, 0, 0), new TimeSpan(7, 0, 0), new TimeSpan(3, 0, 0)) &&
                ThemeScheduler.IsDarkNow(new TimeSpan(19, 0, 0), new TimeSpan(7, 0, 0), new TimeSpan(8, 0, 0));
            Check(lines, ref failures, scheduleOk, "主题时间判断逻辑（含跨午夜）");

            // 主题模式 / 位置 / 城市
            string beforeMode = Settings.LoadThemeMode();
            double beforeLat;
            double beforeLon;
            bool beforeHasLocation = Settings.LoadThemeLocation(out beforeLat, out beforeLon);
            string beforeCity = Settings.LoadThemeCity();

            Settings.SaveThemeMode("sunrise");
            Settings.SaveThemeLocation(39.9042, 116.4074);
            Settings.SaveThemeCity("北京");
            double readLat;
            double readLon;
            bool modeOk = Settings.LoadThemeMode() == "sunrise" &&
                          Settings.LoadThemeLocation(out readLat, out readLon) &&
                          Math.Abs(readLat - 39.9042) < 0.001 &&
                          Math.Abs(readLon - 116.4074) < 0.001 &&
                          Settings.LoadThemeCity() == "北京";
            Check(lines, ref failures, modeOk, "主题模式 / 坐标 / 城市读写");

            Settings.SaveThemeMode(beforeMode);
            if (beforeHasLocation) Settings.SaveThemeLocation(beforeLat, beforeLon);
            else Settings.ClearThemeLocation();
            Settings.SaveThemeCity(beforeCity);

            // 日出日落计算
            TimeSpan sunriseTime;
            TimeSpan sunsetTime;
            bool sunOk = SunCalculator.TryCompute(new DateTime(2026, 11, 5), 39.9042, 116.4074, out sunriseTime, out sunsetTime)
                && sunriseTime.TotalHours > 4.0 && sunriseTime.TotalHours < 9.0
                && sunsetTime.TotalHours > 15.0 && sunsetTime.TotalHours < 21.0;
            Check(lines, ref failures, sunOk, "日出日落计算（北京 2026-11-05）");
            if (sunOk)
            {
                lines.Add("        北京: 日出 " + ThemeScheduler.FormatTime(sunriseTime) +
                          " / 日落 " + ThemeScheduler.FormatTime(sunsetTime));
            }

            // Windows 夜间模式状态解析（字节级测试）
            bool parseOn;
            bool parseOff;
            bool nightLightOk =
                NightLightState.TryParseState(BuildCloudStorePayload(new byte[] { 0x43, 0x42, 0x01, 0x00, 0x10, 0x00, 0x00 }), out parseOn) && parseOn &&
                NightLightState.TryParseState(BuildCloudStorePayload(new byte[] { 0x43, 0x42, 0x01, 0x00, 0x00 }), out parseOff) && !parseOff;
            Check(lines, ref failures, nightLightOk, "Windows 夜间模式状态解析");

            bool realNightLightOn;
            if (NightLightState.TryIsOn(out realNightLightOn))
            {
                lines.Add("        当前系统夜间模式: " + (realNightLightOn ? "开启" : "关闭"));
            }
            else
            {
                lines.Add("        当前系统夜间模式: 读取不可用（将回退自定义时间）");
            }

            // 4. 开机启动项
            bool beforeAuto = Settings.IsAutoStartEnabled();
            bool setOk = false;
            bool clearOk = false;
            string error = null;
            try
            {
                Settings.SetAutoStart(true);
                setOk = Settings.IsAutoStartEnabled() && Settings.IsAutoStartCurrent();
                Settings.SetAutoStart(false);
                clearOk = !Settings.IsAutoStartEnabled();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            try { Settings.SetAutoStart(beforeAuto); }
            catch { }
            Check(lines, ref failures, setOk && clearOk,
                "开机启动项读写（HKCU\\...\\Run\\ScreenStay）" + (error == null ? "" : " 错误: " + error));
            lines.Add("        原始自启状态已恢复: " + beforeAuto);

            // 5. 图标资源（三种状态）
            string[] resources = typeof(SelfTest).Assembly.GetManifestResourceNames();
            string[] iconNames = new string[]
            {
                "KeepAwake.Resources.icon-on.ico",
                "KeepAwake.Resources.icon-monitor.ico",
                "KeepAwake.Resources.icon-off.ico"
            };
            bool allIcons = true;
            for (int i = 0; i < iconNames.Length; i++)
            {
                if (Array.IndexOf(resources, iconNames[i]) < 0) allIcons = false;
            }
            Check(lines, ref failures, allIcons, "托盘图标资源已嵌入（播放中 / 监测中 / 已关闭）");

            // 6. 图标可解析
            bool iconsLoad = true;
            for (int i = 0; i < iconNames.Length; i++)
            {
                try
                {
                    using (Stream stream = typeof(SelfTest).Assembly.GetManifestResourceStream(iconNames[i]))
                    {
                        if (stream == null)
                        {
                            iconsLoad = false;
                            continue;
                        }
                        using (Icon icon = new Icon(stream, SystemInformation.SmallIconSize))
                        {
                            if (icon.Width < 16 || icon.Height < 16) iconsLoad = false;
                        }
                    }
                }
                catch
                {
                    iconsLoad = false;
                }
            }
            Check(lines, ref failures, iconsLoad,
                "图标可按 " + SystemInformation.SmallIconSize.Width + "px 解析");

            // 7. WinUI 3 主界面（可选组件，仅提示）
            string viewerPath = Program.FindViewerPath();
            lines.Add("        WinUI 3 主界面: " +
                (viewerPath == null ? "未找到" : viewerPath));

            lines.Add("");
            lines.Add(failures == 0 ? "结果: 全部通过 (PASS)" : "结果: " + failures + " 项失败 (FAIL)");

            string reportPath = Path.Combine(Path.GetTempPath(), "KeepAwake-selftest.txt");
            if (args != null && args.Length > 1 && !string.IsNullOrEmpty(args[1])
                && !args[1].StartsWith("-", StringComparison.Ordinal))
            {
                reportPath = args[1];
            }
            try
            {
                File.WriteAllLines(reportPath, lines.ToArray(), new UTF8Encoding(true));
            }
            catch
            {
            }

            return failures == 0 ? 0 : 1;
        }

        /// <summary>构造一个最小的 CloudStore 外层信封，用于夜间模式解析自检。</summary>
        private static byte[] BuildCloudStorePayload(byte[] inner)
        {
            List<byte> buffer = new List<byte>();
            buffer.AddRange(new byte[] { 0x43, 0x42, 0x01, 0x00 });
            buffer.AddRange(new byte[] { 0x0A, 0x02, 0x01, 0x00 });
            buffer.Add(0x2A);
            buffer.Add(0x06);
            buffer.Add(0x00);
            buffer.Add(0x2A);
            buffer.Add(0x2B);
            buffer.Add(0x0E);
            buffer.Add((byte)inner.Length);
            buffer.AddRange(inner);
            buffer.Add(0x00);
            buffer.Add(0x00);
            buffer.Add(0x00);
            return buffer.ToArray();
        }

        private static void Check(List<string> lines, ref int failures, bool ok, string title)
        {
            if (ok)
            {
                lines.Add("[PASS] " + title);
            }
            else
            {
                failures++;
                lines.Add("[FAIL] " + title);
            }
        }
    }
}
