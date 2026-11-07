using System;
using System.IO;
using Microsoft.Win32;

namespace KeepAwake.Viewer
{
    /// <summary>主程序（托盘 exe）与会话相关路径。</summary>
    internal static class CorePaths
    {
        internal static string InstallDirectory
        {
            get
            {
                try
                {
                    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                }
                catch
                {
                    return AppContext.BaseDirectory;
                }
            }
        }

        internal static string CoreExePath
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\ScreenStay", false))
                    {
                        if (key != null)
                        {
                            string recorded = key.GetValue("CorePath") as string;
                            if (!string.IsNullOrEmpty(recorded) && File.Exists(recorded)) return recorded;
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    string candidate = Path.Combine(InstallDirectory, "ScreenStay.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
                return null;
            }
        }

        internal static string HelpFilePath
        {
            get
            {
                try
                {
                    string path = Path.Combine(InstallDirectory, "使用说明.txt");
                    if (File.Exists(path)) return path;
                    path = Path.Combine(InstallDirectory, "使用说明.md");
                    if (File.Exists(path)) return path;
                }
                catch
                {
                }
                return null;
            }
        }
    }
}
