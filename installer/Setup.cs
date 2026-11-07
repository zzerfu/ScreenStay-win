using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using WpfImage = System.Windows.Controls.Image;

namespace ScreenStay.Installer
{
    internal static class Program
    {
        internal const string AppName = "ScreenStay";
        internal const string AppVersion = "1.5.3";
        internal const string Publisher = "ZzerFu";
        private const string PayloadResourceName = "ScreenStay.Installer.payload.zip";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenStay";

        internal static string LastError = string.Empty;

        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = HasArgument(args, "/S") || HasArgument(args, "/SILENT") || HasArgument(args, "/quiet");
            string target = GetTargetFromArgs(args);
            if (string.IsNullOrEmpty(target))
            {
                target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs\\" + AppName);
            }

            if (silent)
            {
                bool ok = Install(target, false, true, false, true);
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            SetupController controller = new SetupController(target);
            app.Run(controller.Window);
        }

        private static bool HasArgument(string[] args, string name)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetTargetFromArgs(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(3).Trim('"');
                }
                if (string.Equals(args[i], "/DIR", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1].Trim('"');
                }
            }
            return null;
        }

        internal static bool Install(string target, bool createDesktopShortcut, bool enableAutostart, bool launchApp,
            bool createStartMenuShortcut)
        {
            LastError = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(target)) { LastError = "安装目录为空。"; return false; }
                target = Path.GetFullPath(target.TrimEnd('\\'));
                if (target.Length < 4) { LastError = "安装目录无效。"; return false; }

                KillAppProcesses();

                string tempDir = Path.Combine(Path.GetTempPath(), "ScreenStay-install");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                string zipPath = Path.Combine(tempDir, AppName + "-payload.zip");
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName))
                {
                    if (resource == null) { LastError = "安装资源缺失。"; return false; }
                    using (FileStream file = File.Create(zipPath))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = resource.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            file.Write(buffer, 0, read);
                        }
                    }
                }

                string extractDir = Path.Combine(tempDir, "files");
                Directory.CreateDirectory(extractDir);
                using (FileStream zipStream = File.OpenRead(zipPath))
                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string relative = entry.FullName.Replace('/', '\\');
                        if (relative.EndsWith("\\", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(Path.Combine(extractDir, relative));
                            continue;
                        }
                        string destination = Path.Combine(extractDir, relative);
                        string parent = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        entry.ExtractToFile(destination, true);
                    }
                }

                if (Directory.Exists(target)) Directory.Delete(target, true);
                CopyDirectory(extractDir, target);
                Directory.Delete(tempDir, true);

                string exePath = Path.Combine(target, AppName + ".exe");
                if (!File.Exists(exePath)) { LastError = "安装文件不完整。"; return false; }

                if (createStartMenuShortcut)
                {
                    CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                        AppName + ".lnk"), exePath, target);
                }
                if (createDesktopShortcut)
                {
                    CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        AppName + ".lnk"), exePath, target);
                }

                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (enableAutostart)
                    {
                        run.SetValue(AppName, "\"" + exePath + "\" --startup", RegistryValueKind.String);
                    }
                    else
                    {
                        run.DeleteValue(AppName, false);
                    }
                    run.DeleteValue("KeepAwake", false);
                }

                WriteUninstallEntry(target, exePath);

                if (launchApp)
                {
                    try { Process.Start(exePath); }
                    catch { }
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            string[] files = Directory.GetFiles(sourceDir);
            for (int i = 0; i < files.Length; i++)
            {
                File.Copy(files[i], Path.Combine(targetDir, Path.GetFileName(files[i])), true);
            }
            string[] directories = Directory.GetDirectories(sourceDir);
            for (int i = 0; i < directories.Length; i++)
            {
                CopyDirectory(directories[i], Path.Combine(targetDir, Path.GetFileName(directories[i])));
            }
        }

        private static void CreateShortcut(string shortcutPath, string exePath, string workingDirectory)
        {
            try
            {
                string directory = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exePath + ",0" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, new object[0]);
            }
            catch
            {
            }
        }

        private static void WriteUninstallEntry(string target, string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                key.SetValue("DisplayName", AppName, RegistryValueKind.String);
                key.SetValue("DisplayVersion", AppVersion, RegistryValueKind.String);
                key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                key.SetValue("InstallLocation", target, RegistryValueKind.String);
                key.SetValue("DisplayIcon", exePath + ",0", RegistryValueKind.String);
                string uninstallExe = Path.Combine(target, "Uninstall.exe");
                key.SetValue("UninstallString", "\"" + uninstallExe + "\"", RegistryValueKind.String);
                key.SetValue("QuietUninstallString", "\"" + uninstallExe + "\" /S", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);

                long total = 0;
                try
                {
                    string[] files = Directory.GetFiles(target, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < files.Length; i++)
                    {
                        try { total += new FileInfo(files[i]).Length; }
                        catch { }
                    }
                }
                catch { }
                key.SetValue("EstimatedSize", (int)(total / 1024), RegistryValueKind.DWord);
            }
        }

        private static void KillAppProcesses()
        {
            string[] names = new string[] { AppName, "ScreenStay.Viewer", "KeepAwake", "KeepAwake.Viewer" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(names[i]);
                    for (int j = 0; j < processes.Length; j++)
                    {
                        try
                        {
                            if (processes[j].Id != Process.GetCurrentProcess().Id) processes[j].Kill();
                        }
                        catch { }
                        finally
                        {
                            try { processes[j].Dispose(); }
                            catch { }
                        }
                    }
                }
                catch
                {
                }
            }
        }
    }

    internal sealed class SetupController
    {
        private readonly Window _window;
        private readonly string _defaultTarget;

        private TextBox _dirBox;
        private Button _browseButton;
        private Button _installButton;
        private Button _cancelButton;
        private Button _closeButton;
        private Button _openButton;
        private Button _finishButton;
        private CheckBox _desktopCheck;
        private CheckBox _autostartCheck;
        private CheckBox _launchCheck;
        private TextBlock _statusText;
        private TextBlock _successPathText;
        private Grid _installPanel;
        private Grid _successPanel;
        private StackPanel _footerButtons;
        private Border _header;
        private string _installedExe;

        internal Window Window
        {
            get { return _window; }
        }

        internal SetupController(string defaultTarget)
        {
            _defaultTarget = defaultTarget;
            _window = LoadWindow();
            _window.Icon = LoadWindowIcon();
            BindElements();
            ApplyIcon();
            WireEvents();
            _dirBox.Text = _defaultTarget;
            _statusText.Text = "点击「安装」开始，或先修改安装目录。";
        }

        private static Window LoadWindow()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenStay.Installer.SetupWindow.xaml"))
            {
                if (stream == null) throw new InvalidOperationException("SetupWindow.xaml resource missing.");
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    return (Window)XamlReader.Load(reader);
                }
            }
        }

        private void BindElements()
        {
            _dirBox = Find<TextBox>("DirBox");
            _browseButton = Find<Button>("BrowseButton");
            _installButton = Find<Button>("InstallButton");
            _cancelButton = Find<Button>("CancelButton");
            _closeButton = Find<Button>("CloseButton");
            _openButton = Find<Button>("OpenButton");
            _finishButton = Find<Button>("FinishButton");
            _desktopCheck = Find<CheckBox>("DesktopCheck");
            _autostartCheck = Find<CheckBox>("AutoStartCheck");
            _launchCheck = Find<CheckBox>("LaunchCheck");
            _statusText = Find<TextBlock>("StatusText");
            _successPathText = Find<TextBlock>("SuccessPathText");
            _installPanel = Find<Grid>("InstallPanel");
            _successPanel = Find<Grid>("SuccessPanel");
            _footerButtons = Find<StackPanel>("FooterButtons");
            _header = Find<Border>("Header");
        }

        private T Find<T>(string name) where T : class
        {
            return _window.FindName(name) as T;
        }

        private void ApplyIcon()
        {
            WpfImage image = Find<WpfImage>("AppIcon");
            ImageSource source = LoadWindowIcon();
            if (image != null && source != null) image.Source = source;
        }

        private static ImageSource LoadWindowIcon()
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName))
                {
                    if (icon == null) return null;
                    return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch
            {
                return null;
            }
        }

        private void WireEvents()
        {
            _closeButton.Click += delegate { _window.Close(); };
            _cancelButton.Click += delegate { _window.Close(); };
            _finishButton.Click += delegate { _window.Close(); };
            _installButton.Click += delegate { StartInstall(); };
            _openButton.Click += delegate { OpenInstalledApp(); };
            _browseButton.Click += delegate { BrowseFolder(); };
            _header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { _window.DragMove(); }
                    catch { }
                }
            };
        }

        private void BrowseFolder()
        {
            using (WinForms.FolderBrowserDialog dialog = new WinForms.FolderBrowserDialog())
            {
                dialog.Description = "选择 ScreenStay 安装目录";
                dialog.ShowNewFolderButton = true;
                string current = _dirBox.Text.Trim();
                if (Directory.Exists(current)) dialog.SelectedPath = current;
                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    _dirBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void StartInstall()
        {
            string target = _dirBox.Text.Trim();
            if (target.Length == 0)
            {
                _statusText.Text = "请选择安装目录。";
                return;
            }

            _installButton.IsEnabled = false;
            _browseButton.IsEnabled = false;
            _desktopCheck.IsEnabled = false;
            _autostartCheck.IsEnabled = false;
            _launchCheck.IsEnabled = false;
            _statusText.Text = "正在安装，请稍候…";

            bool ok = Program.Install(target,
                _desktopCheck.IsChecked == true,
                _autostartCheck.IsChecked == true,
                _launchCheck.IsChecked == true,
                true);

            if (!ok)
            {
                _statusText.Text = "安装失败：" + Program.LastError;
                _installButton.IsEnabled = true;
                _browseButton.IsEnabled = true;
                _desktopCheck.IsEnabled = true;
                _autostartCheck.IsEnabled = true;
                _launchCheck.IsEnabled = true;
                return;
            }

            _installedExe = Path.Combine(Path.GetFullPath(target.TrimEnd('\\')), Program.AppName + ".exe");
            _installPanel.Visibility = Visibility.Collapsed;
            _successPanel.Visibility = Visibility.Visible;
            _footerButtons.Visibility = Visibility.Collapsed;
            _successPathText.Text = "已安装到 " + Path.GetDirectoryName(_installedExe);
        }

        private void OpenInstalledApp()
        {
            try
            {
                if (!string.IsNullOrEmpty(_installedExe) && File.Exists(_installedExe))
                {
                    Process.Start(_installedExe);
                }
            }
            catch
            {
            }
            _window.Close();
        }
    }
}
