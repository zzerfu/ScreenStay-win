using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenStay.Uninstaller
{
    internal static class Program
    {
        private const string AppName = "ScreenStay";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenStay";
        private const string SettingsKeyPath = @"Software\ScreenStay";
        private const string LegacySettingsKeyPath = @"Software\KeepAwake";
        private const string LegacyRunValueName = "KeepAwake";

        private static bool _keepSettings = true;

        [STAThread]
        private static void Main(string[] args)
        {
            bool silent = HasArgument(args, "/S") || HasArgument(args, "/SILENT") || HasArgument(args, "/quiet");
            string installDir = GetInstallDirectory();

            if (string.IsNullOrEmpty(installDir) || installDir.TrimEnd('\\').Length < 4)
            {
                if (!silent) MessageBox.Show("找不到 ScreenStay 的安装目录。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!silent)
            {
                using (Form form = BuildForm(installDir))
                {
                    if (form.ShowDialog() != DialogResult.OK) return;
                }
            }

            try
            {
                DoUninstall(installDir);
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show("卸载时出错：\r\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!silent)
            {
                MessageBox.Show("ScreenStay 已卸载。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            ScheduleDirectoryDelete(installDir);
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

        private static Form BuildForm(string installDir)
        {
            Form form = new Form();
            form.Text = "卸载 ScreenStay";
            form.Font = new System.Drawing.Font("Segoe UI", 9.5f);
            form.ClientSize = new System.Drawing.Size(470, 230);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowIcon = true;

            Label title = new Label();
            title.Text = "卸载 ScreenStay";
            title.Font = new System.Drawing.Font("Segoe UI", 14f, System.Drawing.FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new System.Drawing.Point(20, 16);
            form.Controls.Add(title);

            Label hint = new Label();
            hint.Text = "将从本机移除 ScreenStay 及其开机启动项、快捷方式。";
            hint.AutoSize = true;
            hint.Location = new System.Drawing.Point(22, 52);
            form.Controls.Add(hint);

            Label path = new Label();
            path.Text = "安装目录：" + installDir;
            path.AutoSize = false;
            path.Size = new System.Drawing.Size(420, 34);
            path.Location = new System.Drawing.Point(22, 80);
            form.Controls.Add(path);

            CheckBox keep = new CheckBox();
            keep.Text = "保留设置（推荐，下次安装可继续使用）";
            keep.Checked = true;
            keep.AutoSize = true;
            keep.Location = new System.Drawing.Point(22, 120);
            form.Controls.Add(keep);

            Button uninstall = new Button();
            uninstall.Text = "卸载";
            uninstall.Size = new System.Drawing.Size(100, 32);
            uninstall.Location = new System.Drawing.Point(242, 175);
            uninstall.DialogResult = DialogResult.OK;
            form.Controls.Add(uninstall);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Size = new System.Drawing.Size(100, 32);
            cancel.Location = new System.Drawing.Point(350, 175);
            cancel.DialogResult = DialogResult.Cancel;
            form.Controls.Add(cancel);

            form.AcceptButton = uninstall;
            form.CancelButton = cancel;
            form.FormClosing += delegate
            {
                _keepSettings = keep.Checked;
            };
            return form;
        }

        private static string GetInstallDirectory()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, false))
                {
                    if (key != null)
                    {
                        string location = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(location) && Directory.Exists(location)) return location;
                    }
                }
            }
            catch
            {
            }
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        private static void DoUninstall(string installDir)
        {
            KillAppProcesses();

            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (run != null)
                    {
                        run.DeleteValue("ScreenStay", false);
                        run.DeleteValue(LegacyRunValueName, false);
                    }
                }
            }
            catch
            {
            }

            DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ScreenStay.lnk"));
            DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ScreenStay.lnk"));
            DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "ScreenStay.lnk"));

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
            }
            catch
            {
            }

            if (!_keepSettings)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(SettingsKeyPath, false); }
                catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(LegacySettingsKeyPath, false); }
                catch { }
            }
        }

        private static void DeleteShortcut(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static void KillAppProcesses()
        {
            string[] names = new string[] { "ScreenStay", "ScreenStay.Viewer", "KeepAwake", "KeepAwake.Viewer" };
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

        private static void ScheduleDirectoryDelete(string installDir)
        {
            try
            {
                string args = "/c ping 127.0.0.1 -n 3 >NUL & rmdir /s /q \"" + installDir + "\"";
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "cmd.exe";
                info.Arguments = args;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(info);
            }
            catch
            {
            }
        }
    }
}
