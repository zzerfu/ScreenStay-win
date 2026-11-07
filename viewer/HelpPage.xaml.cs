using System;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml.Controls;

namespace KeepAwake.Viewer
{
    public sealed partial class HelpPage : Page
    {
        public HelpPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => LoadHelp();
        }

        private void LoadHelp()
        {
            try
            {
                string path = CorePaths.HelpFilePath;
                if (!string.IsNullOrEmpty(path))
                {
                    HelpText.Text = File.ReadAllText(path, Encoding.UTF8);
                    return;
                }
            }
            catch
            {
            }

            HelpText.Text =
                "ScreenStay —— 智能屏幕常亮小工具\r\n" +
                "\r\n" +
                "一、自动防息屏\r\n" +
                "  开启后检测到音乐 / 视频播放时保持屏幕常亮；没有播放时交给 Windows 电源管理。\r\n" +
                "  可在主界面「常规」或托盘右键菜单中开关。\r\n" +
                "\r\n" +
                "二、托盘图标\r\n" +
                "  · 单击：打开主界面\r\n" +
                "  · 双击：快速开关自动防息屏\r\n" +
                "  · 右键：当前状态 / 自动防息屏开关 / 打开主页面 / 退出\r\n" +
                "  · 红色显示器：已开启，检测到播放；绿色显示器：正常；灰色显示器：未开启\r\n" +
                "\r\n" +
                "三、主题\r\n" +
                "  主界面「主题」中可选择 4 种自动切换方式：自定义时间、日出日落（自动定位）、\r\n" +
                "  日出日落（手动选择城市）、跟随 Windows 夜间模式。\r\n" +
                "\r\n" +
                "四、阻止休眠的程序\r\n" +
                "  主界面「阻止休眠的程序」→ 获取电源请求（需要管理员权限，会弹出 UAC）。\r\n" +
                "\r\n" +
                "五、其他\r\n" +
                "  · 开机自动启动、弹窗提醒在「常规」中设置\r\n" +
                "  · 配置保存在 HKCU\\Software\\ScreenStay" + Environment.NewLine +
                "  · 自检命令：ScreenStay.exe --selftest 报告.txt\r\n" +
                "\r\n" +
                "（没有找到安装目录下的「使用说明.txt」，以上为内置摘要。）";
        }
    }
}
