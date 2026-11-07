using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace KeepAwake.Viewer
{
    public sealed partial class RequestsPage : Page
    {
        private const string ReportFileName = "KeepAwake-powerrequests.txt";

        private string _rawReport = string.Empty;
        private bool _detailsVisible;

        public RequestsPage()
        {
            this.InitializeComponent();
            ApplyFriendlySummary(string.Empty);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string path = e.Parameter as string;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                LoadReportFromFile(path);
                return;
            }

            string cached = Path.Combine(Path.GetTempPath(), ReportFileName);
            if (File.Exists(cached))
            {
                LoadReportFromFile(cached);
                StatusText.Text = "上次检测结果";
            }
        }

        private void LoadReportFromFile(string path)
        {
            try
            {
                SetReport(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                ShowHint(InfoBarSeverity.Error, "读取数据失败", ex.Message);
            }
        }

        private void SetReport(string body)
        {
            _rawReport = body ?? string.Empty;
            ReportBox.Text = "原始输出（powercfg /requests）：\r\n\r\n" + NormalizeLineEndings(_rawReport);
            ApplyFriendlySummary(_rawReport);
        }

        private void ApplyFriendlySummary(string raw)
        {
            List<string> displayNames;
            List<string> systemNames;
            ParseReport(raw, out displayNames, out systemNames);
            DisplaySummaryText.Text = FormatNames(displayNames, "屏幕关闭");
            SystemSummaryText.Text = FormatNames(systemNames, "系统睡眠");
        }

        private static void ParseReport(string raw, out List<string> displayNames, out List<string> systemNames)
        {
            displayNames = new List<string>();
            systemNames = new List<string>();
            if (string.IsNullOrEmpty(raw)) return;

            string section = null;
            string[] lines = NormalizeLineEndings(raw).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                string header = line.TrimEnd(':', '：').Trim();
                bool isHeader = line.EndsWith(":", StringComparison.Ordinal) ||
                                line.EndsWith("：", StringComparison.Ordinal);
                if (isHeader)
                {
                    if (IsDisplayHeader(header)) section = "display";
                    else if (IsSystemHeader(header)) section = "system";
                    else section = "other"; // AWAYMODE / 执行 / PERFBOOST 等其它段落，忽略
                    continue;
                }

                if (section == null || section == "other") continue;
                if (IsIgnoredLine(line)) continue;

                string name = ExtractProgramName(line);
                if (string.IsNullOrEmpty(name)) continue;
                List<string> target = section == "display" ? displayNames : systemNames;
                if (!target.Contains(name)) target.Add(name);
            }
        }

        private static bool IsDisplayHeader(string header)
        {
            return header.Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                   header.StartsWith("显示", StringComparison.Ordinal);
        }

        private static bool IsSystemHeader(string header)
        {
            return header.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
                   header.StartsWith("系统", StringComparison.Ordinal);
        }

        private static bool IsIgnoredLine(string line)
        {
            if (line.Equals("None.", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.Equals("无。", StringComparison.Ordinal) || line.Equals("无", StringComparison.Ordinal)) return true;
            if (line.StartsWith("exit code", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith("powercfg", StringComparison.OrdinalIgnoreCase)) return true;
            if (line.StartsWith("----", StringComparison.Ordinal)) return true;
            return false;
        }

        private static string ExtractProgramName(string line)
        {
            string text = line.Trim();
            if (text.Contains("[PROCESS]") || text.Contains("[进程]"))
            {
                int bracket = text.IndexOf(']');
                if (bracket >= 0) text = text.Substring(bracket + 1).Trim();
            }

            text = text.Trim('"', ' ');
            int slash = text.LastIndexOf('\\');
            int slash2 = text.LastIndexOf('/');
            if (slash2 > slash) slash = slash2;
            if (slash >= 0) text = text.Substring(slash + 1).Trim();
            return text;
        }

        private static string FormatNames(List<string> names, string what)
        {
            if (names.Count == 0) return "未检测到正在阻止" + what + "的程序。";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append("· ").Append(names[i]);
            }
            return sb.ToString();
        }

        private static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text.Replace("\n", "\r\n");
        }

        private void Details_Click(object sender, RoutedEventArgs e)
        {
            _detailsVisible = !_detailsVisible;
            DetailsBorder.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
            DetailsButton.Content = _detailsVisible ? "隐藏详细信息" : "显示详细信息";
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            string core = CorePaths.CoreExePath;
            if (string.IsNullOrEmpty(core))
            {
                ShowHint(InfoBarSeverity.Error, "未找到主程序",
                    "请把 ScreenStay.exe 和 ScreenStay.Viewer 放在同一目录后重试。");
                return;
            }

            RefreshButton.IsEnabled = false;
            StatusText.Text = "正在把求管理员权限…";
            HintBar.IsOpen = false;

            string path = Path.Combine(Path.GetTempPath(), ReportFileName);
            try
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                }

                ProcessLaunchResult launch = await RunElevatedDumpAsync(core, path);
                if (!launch.Started)
                {
                    StatusText.Text = string.Empty;
                    if (launch.Cancelled)
                    {
                        ShowHint(InfoBarSeverity.Warning, "已取消",
                            "查看系统电源把求需要管理员权限，把在「用户账户控制」窗口中选择「是」。");
                    }
                    else
                    {
                        ShowHint(InfoBarSeverity.Error, "获取失败", launch.Error);
                    }
                    return;
                }

                string content = null;
                for (int i = 0; i < 25 && content == null; i++)
                {
                    if (File.Exists(path))
                    {
                        try { content = File.ReadAllText(path, Encoding.UTF8); }
                        catch { }
                    }
                    if (content == null) await System.Threading.Tasks.Task.Delay(200);
                }

                if (string.IsNullOrEmpty(content))
                {
                    ApplyFriendlySummary(string.Empty);
                    ShowHint(InfoBarSeverity.Warning, "没有获取到数据",
                        "可能程序没有获得管理员权限，或当前系统没有电源把求信息。");
                    return;
                }

                SetReport(content);
                StatusText.Text = "更新于 " + DateTime.Now.ToString("HH:mm:ss");
                ShowHint(InfoBarSeverity.Success, "已更新", "已读取当前系统的电源把求。");
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private sealed class ProcessLaunchResult
        {
            internal bool Started;
            internal bool Cancelled;
            internal string Error;
        }

        private static System.Threading.Tasks.Task<ProcessLaunchResult> RunElevatedDumpAsync(string core, string path)
        {
            return System.Threading.Tasks.Task.Run(new System.Func<ProcessLaunchResult>(delegate
            {
                ProcessLaunchResult result = new ProcessLaunchResult();
                try
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                    psi.FileName = core;
                    psi.Arguments = "--dumprequests \"" + path + "\"";
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

                    using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
                    {
                        result.Started = process != null;
                        if (process != null && !process.WaitForExit(30000))
                        {
                            try { process.Kill(); }
                            catch { }
                        }
                    }
                }
                catch (Win32Exception ex)
                {
                    result.Started = false;
                    result.Cancelled = ex.NativeErrorCode == 1223;
                    result.Error = ex.Message;
                }
                catch (Exception ex)
                {
                    result.Started = false;
                    result.Error = ex.Message;
                }
                return result;
            }));
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string summary =
                    "屏幕显示（阻止屏幕关闭）：\r\n" + DisplaySummaryText.Text + "\r\n\r\n" +
                    "系统睡眠（阻止系统睡眠）：\r\n" + SystemSummaryText.Text + "\r\n\r\n" +
                    ReportBox.Text;
                DataPackage package = new DataPackage();
                package.SetText(summary);
                Clipboard.SetContent(package);
                ShowHint(InfoBarSeverity.Success, "已复制", "内容已复制到剪贴板。");
            }
            catch (Exception ex)
            {
                ShowHint(InfoBarSeverity.Error, "复制失败", ex.Message);
            }
        }

        private void ShowHint(InfoBarSeverity severity, string title, string message)
        {
            HintBar.Severity = severity;
            HintBar.Title = title;
            HintBar.Message = message;
            HintBar.IsOpen = true;
        }
    }
}
