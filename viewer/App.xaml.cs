using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Windows.Devices.Geolocation;
namespace KeepAwake.Viewer
{
    public partial class App : Application
    {
        private const string MainWindowTitle = "ScreenStay 主界面";
        private const string MainMutexName = @"Local\ScreenStay.MainWindow";

        private Window _window;
        private static Mutex _mutex;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            string[] commandLine = Environment.GetCommandLineArgs();
            bool geoTest = false;
            bool requestsMode = false;
            string initialPage = "general";
            string reportPath = null;

            for (int i = 0; i < commandLine.Length; i++)
            {
                string arg = commandLine[i];
                if (string.Equals(arg, "--geotest", StringComparison.OrdinalIgnoreCase))
                {
                    geoTest = true;
                }
                else if (string.Equals(arg, "--requests", StringComparison.OrdinalIgnoreCase))
                {
                    requestsMode = true;
                    if (i + 1 < commandLine.Length && !commandLine[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        reportPath = commandLine[i + 1];
                    }
                }
                else if (string.Equals(arg, "--theme", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase))
                {
                    initialPage = "theme";
                }
                else if (string.Equals(arg, "--main", StringComparison.OrdinalIgnoreCase))
                {
                    initialPage = "general";
                }
            }

            if (geoTest)
            {
                RunGeoTest(commandLine);
                return;
            }

            bool createdNew;
            _mutex = new Mutex(true, MainMutexName, out createdNew);
            if (!createdNew)
            {
                ActivateExistingMainWindow();
                Environment.Exit(0);
                return;
            }

            if (requestsMode) initialPage = "requests";
            _window = new MainWindow(initialPage, reportPath);
            _window.Activate();
        }

        internal static void ActivateExistingMainWindow()
        {
            try
            {
                IntPtr hwnd = FindWindow(null, MainWindowTitle);
                if (hwnd == IntPtr.Zero) return;
                if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
                SetForegroundWindow(hwnd);
            }
            catch
            {
            }
        }

        // 诊断用：KeepAwake.Viewer.exe --geotest <结果文件>，测试定位是否可用
        private static async void RunGeoTest(string[] commandLine)
        {
            string reportPath = Path.Combine(Path.GetTempPath(), "KeepAwake-geotest.txt");
            if (commandLine.Length > 1)
            {
                string candidate = commandLine[commandLine.Length - 1];
                if (candidate.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) reportPath = candidate;
            }

            try
            {
                GeolocationAccessStatus access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed)
                {
                    File.WriteAllText(reportPath, "access=" + access);
                }
                else
                {
                    Geolocator locator = new Geolocator();
                    locator.DesiredAccuracyInMeters = 3000;
                    Geoposition position = await locator.GetGeopositionAsync();
                    File.WriteAllText(reportPath,
                        "ok lat=" + position.Coordinate.Point.Position.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " lon=" + position.Coordinate.Point.Position.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(reportPath, "error: " + ex.Message);
                }
                catch
                {
                }
            }
            Environment.Exit(0);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
