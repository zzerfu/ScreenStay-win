using System;
using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace KeepAwake.Viewer
{
    public sealed partial class GeneralPage : Page
    {
        private const string SettingsKeyPath = @"Software\ScreenStay";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ScreenStay";

        private readonly DispatcherTimer _statusTimer;
        private bool _loading;

        public GeneralPage()
        {
            this.InitializeComponent();
            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(2);
            _statusTimer.Tick += (s, e) => UpdateStatus();
            this.Loaded += (s, e) =>
            {
                LoadSettings();
                _statusTimer.Start();
            };
            this.Unloaded += (s, e) => _statusTimer.Stop();
        }

        private void LoadSettings()
        {
            _loading = true;
            AwakeSwitch.IsOn = ReadDword("Awake", 1) != 0;
            NotifySwitch.IsOn = ReadDword("Notifications", 1) != 0;
            AutoStartSwitch.IsOn = IsAutoStartEnabled();
            AwakeHint.Text = AwakeSwitch.IsOn
                ? "已开启：检测到音乐 / 视频播放时保持屏幕常亮（约 3 秒内生效）。"
                : "已关闭：完全交还 Windows 电源管理。";
            NotifyHint.Text = NotifySwitch.IsOn
                ? "会显示启动、开关等气泡 / 通知提醒。"
                : "已关闭：不再显示启动、开关等气泡 / 通知提醒。";
            UpdateRuntimeText();
            UpdateStatus();
            _loading = false;
        }

        private void UpdateStatus()
        {
            // 从注册表同步开关（托盘菜单可能修改了设置）
            bool awake = ReadDword("Awake", 1) != 0;
            bool notify = ReadDword("Notifications", 1) != 0;
            bool autoStart = IsAutoStartEnabled();
            if (AwakeSwitch.IsOn != awake || NotifySwitch.IsOn != notify || AutoStartSwitch.IsOn != autoStart)
            {
                _loading = true;
                AwakeSwitch.IsOn = awake;
                NotifySwitch.IsOn = notify;
                AutoStartSwitch.IsOn = autoStart;
                _loading = false;
                AwakeHint.Text = awake
                    ? "已开启：检测到音乐 / 视频播放时保持屏幕常亮（约 3 秒内生效）。"
                    : "已关闭：完全交还 Windows 电源管理。";
                NotifyHint.Text = notify
                    ? "会显示启动、开关等气泡 / 通知提醒。"
                    : "已关闭：不再显示启动、开关等气泡 / 通知提醒。";
            }

            bool running = false;
            try
            {
                Process[] instances = Process.GetProcessesByName("ScreenStay");
                running = instances != null && instances.Length > 0;
                if (instances != null)
                {
                    for (int i = 0; i < instances.Length; i++)
                    {
                        try { instances[i].Dispose(); }
                        catch { }
                    }
                }
            }
            catch
            {
            }

            string status;
            if (!running)
            {
                status = "ScreenStay 未运行";
            }
            else
            {
                string stored = ReadStatusText();
                if (!string.IsNullOrEmpty(stored))
                {
                    status = stored;
                }
                else
                {
                    status = awake ? "已开启，无音视频播放" : "自动防息屏未开启";
                }
            }

            MonitorStatusText.Text = status;
            MonitorStatusDetail.Text = running
                ? "状态每 2 秒自动刷新；在托盘或主界面修改开关后约 3 秒生效。"
                : "主程序未运行。请启动 ScreenStay.exe，或检查托盘区。";
            UpdatePlayingApps(running, awake);
        }

        private void UpdatePlayingApps(bool running, bool awake)
        {
            if (!running)
            {
                PlayingAppsText.Text = "主程序未运行";
                return;
            }
            if (!awake)
            {
                PlayingAppsText.Text = "自动防息屏未开启";
                return;
            }

            string apps = ReadPlayingApps();
            string[] parts = string.IsNullOrEmpty(apps)
                ? new string[0]
                : apps.Split(new char[] { '、' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                PlayingAppsText.Text = "未检测到播放中的软件";
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i].Trim();
                if (name.Length == 0) continue;
                if (builder.Length > 0) builder.Append('\n');
                builder.Append("· ").Append(name);
            }
            PlayingAppsText.Text = builder.Length == 0 ? "未检测到播放中的软件" : builder.ToString();
        }

        private static string ReadPlayingApps()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
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

        private static string ReadStatusText()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
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

        private static int ReadDword(string name, int fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(name);
                        if (value != null) return Convert.ToInt32(value);
                    }
                }
            }
            catch
            {
            }
            return fallback;
        }

        private static void WriteDword(string name, int value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key != null) key.SetValue(name, value, RegistryValueKind.DWord);
                }
            }
            catch
            {
            }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key != null) return key.GetValue(RunValueName) != null;
                }
            }
            catch
            {
            }
            return false;
        }

        private void AwakeSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            WriteDword("Awake", AwakeSwitch.IsOn ? 1 : 0);
            AwakeHint.Text = AwakeSwitch.IsOn
                ? "已开启：检测到音乐 / 视频播放时保持屏幕常亮（约 3 秒内生效）。"
                : "已关闭：完全交还 Windows 电源管理。";
        }

        private void AutoStartSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try
            {
                if (AutoStartSwitch.IsOn)
                {
                    string core = CorePaths.CoreExePath;
                    if (string.IsNullOrEmpty(core))
                    {
                        throw new InvalidOperationException("未找到 ScreenStay.exe，请把主程序和 ScreenStay.Viewer 放在同一目录。");
                    }
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                    {
                        key.SetValue(RunValueName, "\"" + core + "\" --startup", RegistryValueKind.String);
                    }
                    ShowInfo("已开启开机自动启动", "下次登录 Windows 后会在托盘静默运行。", InfoBarSeverity.Success);
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                    {
                        if (key != null) key.DeleteValue(RunValueName, false);
                    }
                    ShowInfo("已关闭开机自动启动", "已从当前用户的启动项中移除。", InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                _loading = true;
                AutoStartSwitch.IsOn = IsAutoStartEnabled();
                _loading = false;
                ShowInfo("设置开机自动启动失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        private void NotifySwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            WriteDword("Notifications", NotifySwitch.IsOn ? 1 : 0);

            bool actual = ReadDword("Notifications", 1) != 0;
            if (actual != NotifySwitch.IsOn)
            {
                _loading = true;
                NotifySwitch.IsOn = actual;
                _loading = false;
                ShowInfo("设置未生效", "写入注册表失败，请稍后重试。", InfoBarSeverity.Error);
                return;
            }

            NotifyHint.Text = NotifySwitch.IsOn
                ? "会显示启动、开关等气泡 / 通知提醒。"
                : "已关闭：不再显示启动、开关等气泡 / 通知提醒。";
            ShowInfo("弹窗提醒已更新",
                NotifySwitch.IsOn ? "已恢复气泡 / 通知提醒。" : "已关闭气泡 / 通知提醒。",
                InfoBarSeverity.Success);
        }

        private void OpenRequests_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.Instance != null) MainWindow.Instance.SelectPage("requests");
        }

        private void UpdateRuntimeText()
        {
            bool running = false;
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    Process[] instances = Process.GetProcessesByName("ScreenStay");
                    running = instances != null && instances.Length > 0;
                    if (instances != null)
                    {
                        for (int i = 0; i < instances.Length; i++)
                        {
                            try { instances[i].Dispose(); }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
            }

            string core = CorePaths.CoreExePath;
            RuntimeText.Text = "主程序：" + (string.IsNullOrEmpty(core) ? "未找到" : core) +
                               "；当前状态：" + (running ? "正在运行" : "未运行");
        }

        private void ShowInfo(string title, string message, InfoBarSeverity severity)
        {
            GeneralInfoBar.Title = title;
            GeneralInfoBar.Message = message;
            GeneralInfoBar.Severity = severity;
            GeneralInfoBar.IsOpen = true;
        }
    }
}
