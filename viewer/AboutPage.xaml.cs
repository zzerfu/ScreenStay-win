using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace KeepAwake.Viewer
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => LoadInfo();
        }

        private void LoadInfo()
        {
            Version viewerVersion = typeof(App).GetTypeInfo().Assembly.GetName().Version;
            string viewerFull = viewerVersion == null ? "1.4.2.0" : viewerVersion.ToString();
            string viewerShort = viewerVersion == null
                ? "1.4.2"
                : string.Format("{0}.{1}.{2}", viewerVersion.Major, viewerVersion.Minor, viewerVersion.Build);

            string core = CorePaths.CoreExePath;
            string coreVersion = null;
            if (!string.IsNullOrEmpty(core))
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(core);
                    coreVersion = string.IsNullOrEmpty(info.FileVersion) ? info.ProductVersion : info.FileVersion;
                }
                catch
                {
                }
            }

            // 版本号 + 开发者合并为一行
            VersionText.Text = "Version: " + viewerShort + " (" +
                               (string.IsNullOrEmpty(coreVersion) ? viewerFull : coreVersion) + ") by ZzerFu";

            PathText.Text =
                "主程序：" + (string.IsNullOrEmpty(core) ? "未找到" : core) + "\r\n" +
                "安装目录：" + CorePaths.InstallDirectory + "\r\n" +
                "配置项：HKEY_CURRENT_USER\\Software\\ScreenStay\r\n" +
                "自启项：HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\ScreenStay";
        }
    }
}
