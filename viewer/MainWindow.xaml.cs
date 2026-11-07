using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace KeepAwake.Viewer
{
    public sealed partial class MainWindow : Window
    {
        internal static MainWindow Instance;

        private readonly string _initialPage;
        private string _pendingRequestsPath;
        private UISettings _uiSettings;
        private DispatcherTimer _themeTimer;

        internal MainWindow(string initialPage, string requestsPath)
        {
            this.InitializeComponent();
            this.Title = "ScreenStay 主界面";
            Instance = this;

            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (File.Exists(iconPath)) this.AppWindow.SetIcon(iconPath);
            }
            catch
            {
            }

            try
            {
                this.SystemBackdrop = new MicaBackdrop();
            }
            catch
            {
            }

            try
            {
                this.AppWindow.Resize(new SizeInt32(1120, 780));
            }
            catch
            {
            }

            // 标题栏跟随系统深色 / 浅色主题
            try
            {
                _uiSettings = new UISettings();
                _uiSettings.ColorValuesChanged += UISettings_ColorValuesChanged;
            }
            catch
            {
            }

            try
            {
                _themeTimer = new DispatcherTimer();
                _themeTimer.Interval = TimeSpan.FromSeconds(3);
                _themeTimer.Tick += (s, e) => ApplyTitleBarTheme();
                _themeTimer.Start();
            }
            catch
            {
            }

            ApplyTitleBarTheme();
            this.Closed += (s, e) =>
            {
                try { if (_themeTimer != null) _themeTimer.Stop(); }
                catch { }
            };

            _initialPage = string.IsNullOrEmpty(initialPage) ? "general" : initialPage;
            _pendingRequestsPath = requestsPath;

            this.RootGrid.Loaded += MainWindow_Loaded;
        }

        private void ApplyInitialWindowSize()
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                uint dpi = GetDpiForWindow(hwnd);
                double scale = dpi <= 0 ? 1.0 : dpi / 96.0;

                RECT work = new RECT();
                if (!SystemParametersInfo(SPI_GETWORKAREA, 0, ref work, 0)) return;
                int workWidth = work.Right - work.Left;
                int workHeight = work.Bottom - work.Top;

                // 以 1150x800 逻辑像素为基准按 DPI 放大；大分辨率屏幕再按工作区比例放大
                int dpiWidth = (int)Math.Round(1150 * scale);
                int dpiHeight = (int)Math.Round(800 * scale);
                int width = Math.Max(dpiWidth, (int)(workWidth * 0.5));
                int height = Math.Max(dpiHeight, (int)(workHeight * 0.55));
                width = Math.Max(800, Math.Min(width, (int)(workWidth * 0.9)));
                height = Math.Max(600, Math.Min(height, (int)(workHeight * 0.9)));

                int left = work.Left + (workWidth - width) / 2;
                int top = work.Top + (workHeight - height) / 2;
                this.AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
            }
            catch
            {
            }
        }

        private const uint SPI_GETWORKAREA = 0x0030;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

        private void UISettings_ColorValuesChanged(UISettings sender, object args)
        {
            try
            {
                this.DispatcherQueue.TryEnqueue(ApplyTitleBarTheme);
            }
            catch
            {
            }
        }

        private void ApplyTitleBarTheme()
        {
            try
            {
                AppWindowTitleBar titleBar = this.AppWindow.TitleBar;
                titleBar.PreferredTheme = IsSystemDark() ? TitleBarTheme.Dark : TitleBarTheme.Light;
            }
            catch
            {
            }
        }

        private static bool IsSystemDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value != null) return Convert.ToInt32(value) == 0;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyInitialWindowSize();
            SelectNavigationItem(_initialPage);
        }

        private void SelectNavigationItem(string tag)
        {
            foreach (object item in NavView.MenuItems)
            {
                NavigationViewItem navItem = item as NavigationViewItem;
                if (navItem != null && string.Equals(navItem.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    NavView.SelectedItem = navItem;
                    return;
                }
            }
            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }

        /// <summary>供页面内部跳转使用（同时更新左侧导航选中项）。</summary>
        internal void SelectPage(string tag)
        {
            SelectNavigationItem(tag);
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            NavigationViewItem item = args.SelectedItem as NavigationViewItem;
            if (item == null) return;
            NavigateTo(item.Tag as string);
        }

        internal void NavigateTo(string tag)
        {
            if (string.IsNullOrEmpty(tag)) tag = "general";

            if (tag == "theme")
            {
                if (ContentFrame.CurrentSourcePageType != typeof(ThemePage)) ContentFrame.Navigate(typeof(ThemePage));
            }
            else if (tag == "requests")
            {
                string path = _pendingRequestsPath;
                _pendingRequestsPath = null;
                ContentFrame.Navigate(typeof(RequestsPage), path);
            }
            else if (tag == "help")
            {
                if (ContentFrame.CurrentSourcePageType != typeof(HelpPage)) ContentFrame.Navigate(typeof(HelpPage));
            }
            else if (tag == "about")
            {
                if (ContentFrame.CurrentSourcePageType != typeof(AboutPage)) ContentFrame.Navigate(typeof(AboutPage));
            }
            else
            {
                if (ContentFrame.CurrentSourcePageType != typeof(GeneralPage)) ContentFrame.Navigate(typeof(GeneralPage));
            }
        }
    }
}
