using System;
using System.Diagnostics;
using System.Globalization;
using KeepAwake.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.Devices.Geolocation;

namespace KeepAwake.Viewer
{
    public sealed partial class ThemePage : Page
    {
        private const string SettingsKeyPath = @"Software\ScreenStay";
        private const string AutoValueName = "AutoTheme";
        private const string ModeValueName = "ThemeMode";
        private const string DayValueName = "ThemeDayStart";
        private const string NightValueName = "ThemeNightStart";
        private const string LatValueName = "ThemeLat";
        private const string LonValueName = "ThemeLon";
        private const string CityValueName = "ThemeCity";
        private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private sealed class CityInfo
        {
            internal CityInfo(string name, double latitude, double longitude)
            {
                Name = name;
                Latitude = latitude;
                Longitude = longitude;
            }

            internal string Name;
            internal double Latitude;
            internal double Longitude;

            public override string ToString()
            {
                return Name;
            }
        }

        private static readonly CityInfo[] Cities = new CityInfo[]
        {
            new CityInfo("北京", 39.9042, 116.4074),
            new CityInfo("上海", 31.2304, 121.4737),
            new CityInfo("广州", 23.1291, 113.2644),
            new CityInfo("深圳", 22.5431, 114.0579),
            new CityInfo("成都", 30.5728, 104.0668),
            new CityInfo("杭州", 30.2741, 120.1551),
            new CityInfo("武汉", 30.5928, 114.3055),
            new CityInfo("西安", 34.3416, 108.9398),
            new CityInfo("南京", 32.0603, 118.7969),
            new CityInfo("重庆", 29.5630, 106.5516),
            new CityInfo("天津", 39.3434, 117.3616),
            new CityInfo("苏州", 31.2989, 120.5853),
            new CityInfo("长沙", 28.2282, 112.9388),
            new CityInfo("郑州", 34.7466, 113.6254),
            new CityInfo("青岛", 36.0671, 120.3826),
            new CityInfo("大连", 38.9140, 121.6147),
            new CityInfo("厦门", 24.4798, 118.0894),
            new CityInfo("福州", 26.0745, 119.2965),
            new CityInfo("济南", 36.6512, 117.1201),
            new CityInfo("合肥", 31.8206, 117.2272),
            new CityInfo("昆明", 25.0389, 102.7183),
            new CityInfo("沈阳", 41.8057, 123.4315),
            new CityInfo("哈尔滨", 45.8038, 126.5349),
            new CityInfo("长春", 43.8171, 125.3235),
            new CityInfo("石家庄", 38.0428, 114.5149),
            new CityInfo("太原", 37.8706, 112.5489),
            new CityInfo("南昌", 28.6820, 115.8579),
            new CityInfo("南宁", 22.8170, 108.3665),
            new CityInfo("海口", 20.0440, 110.1999),
            new CityInfo("贵阳", 26.6470, 106.6302),
            new CityInfo("兰州", 36.0611, 103.8343),
            new CityInfo("西宁", 36.6171, 101.7782),
            new CityInfo("银川", 38.4872, 106.2309),
            new CityInfo("乌鲁木齐", 43.8256, 87.6168),
            new CityInfo("拉萨", 29.6520, 91.1721),
            new CityInfo("呼和浩特", 40.8426, 111.7492),
            new CityInfo("香港", 22.3193, 114.1694),
            new CityInfo("澳门", 22.1987, 113.5439),
            new CityInfo("台北", 25.0330, 121.5654),
            new CityInfo("东京", 35.6762, 139.6503),
            new CityInfo("首尔", 37.5665, 126.9780),
            new CityInfo("新加坡", 1.3521, 103.8198),
            new CityInfo("伦敦", 51.5074, -0.1278),
            new CityInfo("巴黎", 48.8566, 2.3522),
            new CityInfo("柏林", 52.5200, 13.4050),
            new CityInfo("纽约", 40.7128, -74.0060),
            new CityInfo("洛杉矶", 34.0522, -118.2437),
            new CityInfo("悉尼", -33.8688, 151.2093),
            new CityInfo("莫斯科", 55.7558, 37.6173),
            new CityInfo("迪拜", 25.2048, 55.2708)
        };

        private double _latitude;
        private double _longitude;
        private bool _hasLocation;
        private string _selectedCity = string.Empty;
        private bool _loading;

        public ThemePage()
        {
            this.InitializeComponent();
            foreach (CityInfo city in Cities)
            {
                CityComboBox.Items.Add(city);
            }
            this.Loaded += (s, e) => LoadSettings();
        }

        private void LoadSettings()
        {
            _loading = true;

            bool auto = false;
            string mode = "fixed";
            TimeSpan day = new TimeSpan(7, 0, 0);
            TimeSpan night = new TimeSpan(19, 0, 0);
            string latText = null;
            string lonText = null;
            string city = string.Empty;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key != null)
                    {
                        object autoValue = key.GetValue(AutoValueName);
                        if (autoValue != null) auto = Convert.ToInt32(autoValue) != 0;

                        string modeValue = key.GetValue(ModeValueName) as string;
                        if (!string.IsNullOrEmpty(modeValue)) mode = modeValue.Trim().ToLowerInvariant();

                        day = ParseTime(key.GetValue(DayValueName) as string, day);
                        night = ParseTime(key.GetValue(NightValueName) as string, night);
                        latText = key.GetValue(LatValueName) as string;
                        lonText = key.GetValue(LonValueName) as string;
                        city = key.GetValue(CityValueName) as string;
                        if (city == null) city = string.Empty;
                    }
                }
            }
            catch
            {
            }

            AutoThemeSwitch.IsOn = auto;
            SelectMode(mode);
            DayStartPicker.Time = day;
            NightStartPicker.Time = night;

            double latitude;
            double longitude;
            if (!string.IsNullOrEmpty(latText) && !string.IsNullOrEmpty(lonText) &&
                double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude) &&
                double.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
            {
                _latitude = latitude;
                _longitude = longitude;
                _hasLocation = true;
            }

            _selectedCity = city;
            if (!string.IsNullOrEmpty(city))
            {
                foreach (CityInfo item in Cities)
                {
                    if (item.Name == city)
                    {
                        CityComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            UpdateModePanels();
            UpdateLocationPreview();
            UpdateCityPreview();
            NightLightStatus.Text = BuildNightLightStatus();
            StatusText.Text = "当前 Windows 主题：" + (IsSystemDark() ? "深色" : "浅色") +
                              "，当前时间：" + DateTime.Now.ToString("HH:mm") + "。";
            HintBar.IsOpen = false;

            _loading = false;
        }

        private static TimeSpan ParseTime(string text, TimeSpan fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            string[] parts = text.Trim().Split(':');
            int hour;
            int minute;
            if (parts.Length >= 2 &&
                int.TryParse(parts[0], out hour) &&
                int.TryParse(parts[1], out minute) &&
                hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
            {
                return new TimeSpan(hour, minute, 0);
            }
            return fallback;
        }

        private static string FormatTime(TimeSpan value)
        {
            return value.Hours.ToString("00") + ":" + value.Minutes.ToString("00");
        }

        private static bool IsSystemDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, false))
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

        private void SelectMode(string mode)
        {
            foreach (object item in ModeComboBox.Items)
            {
                ComboBoxItem comboItem = item as ComboBoxItem;
                if (comboItem != null && string.Equals(comboItem.Tag as string, mode, StringComparison.OrdinalIgnoreCase))
                {
                    ModeComboBox.SelectedItem = comboItem;
                    return;
                }
            }
            ModeComboBox.SelectedIndex = 0;
        }

        private string GetSelectedMode()
        {
            ComboBoxItem item = ModeComboBox.SelectedItem as ComboBoxItem;
            if (item == null) return "fixed";
            string tag = item.Tag as string;
            return string.IsNullOrEmpty(tag) ? "fixed" : tag;
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            UpdateModePanels();
            SaveSettings(false);
        }

        private void UpdateModePanels()
        {
            string mode = GetSelectedMode();
            FixedPanel.Visibility = mode == "fixed" ? Visibility.Visible : Visibility.Collapsed;
            LocationPanel.Visibility = mode == "sunrise" ? Visibility.Visible : Visibility.Collapsed;
            CityPanel.Visibility = mode == "city" ? Visibility.Visible : Visibility.Collapsed;
            NightLightPanel.Visibility = mode == "nightlight" ? Visibility.Visible : Visibility.Collapsed;

            if (mode == "nightlight")
            {
                NightLightStatus.Text = BuildNightLightStatus();
            }
        }

        private static string BuildNightLightStatus()
        {
            bool on;
            if (NightLightState.TryIsOn(out on))
            {
                return "Windows 夜间模式当前：" + (on ? "已开启" : "已关闭") +
                       "。\n切换将跟随 Windows 夜间模式：开启时使用深色主题，关闭时使用浅色主题。";
            }
            return "读取不到 Windows 夜间模式状态（可能未配置或系统版本不支持）。\n切换时会自动回退到「自定义时间」。";
        }

        private void UpdateLocationPreview()
        {
            if (!_hasLocation)
            {
                LocationStatus.Text = "尚未获取位置。点击上方按钮获取当前位置。";
                return;
            }

            TimeSpan sunrise;
            TimeSpan sunset;
            if (SunCalculator.TryCompute(DateTime.Now, _latitude, _longitude, out sunrise, out sunset))
            {
                LocationStatus.Text = string.Format(CultureInfo.InvariantCulture,
                    "当前位置：{0:0.0000}, {1:0.0000}\n今天日出 {2}，日落 {3}。",
                    _latitude, _longitude, FormatTime(sunrise), FormatTime(sunset));
            }
            else
            {
                LocationStatus.Text = "无法计算日出日落（极昼 / 极夜或坐标异常）。";
            }
        }

        private void UpdateCityPreview()
        {
            CityInfo city = CityComboBox.SelectedItem as CityInfo;
            if (city == null)
            {
                CityStatus.Text = "选择城市后会自动计算当地的日出 / 日落时间。";
                return;
            }

            TimeSpan sunrise;
            TimeSpan sunset;
            if (SunCalculator.TryCompute(DateTime.Now, city.Latitude, city.Longitude, out sunrise, out sunset))
            {
                CityStatus.Text = string.Format(CultureInfo.InvariantCulture,
                    "今天 {0} 日出 {1}，日落 {2}。", city.Name, FormatTime(sunrise), FormatTime(sunset));
            }
            else
            {
                CityStatus.Text = "无法计算该城市的日出日落。";
            }
        }

        private async void LocateButton_Click(object sender, RoutedEventArgs e)
        {
            LocateButton.IsEnabled = false;
            LocationStatus.Text = "正在请求位置权限…";
            try
            {
                GeolocationAccessStatus access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed)
                {
                    LocationStatus.Text = "无法获取位置（系统位置服务未开启或权限被拒绝）。\n可以改用「手动选择城市」。";
                    return;
                }

                Geolocator locator = new Geolocator();
                locator.DesiredAccuracyInMeters = 3000;
                Geoposition position = await locator.GetGeopositionAsync();

                _latitude = position.Coordinate.Point.Position.Latitude;
                _longitude = position.Coordinate.Point.Position.Longitude;
                _hasLocation = true;
                UpdateLocationPreview();
                SaveSettings(false);
            }
            catch (Exception ex)
            {
                LocationStatus.Text = "获取位置失败：" + ex.Message + "\n可以改用「手动选择城市」。";
            }
            finally
            {
                LocateButton.IsEnabled = true;
            }
        }

        private void CityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            CityComboBox_SelectionChanged_Update();
            UpdateCityPreview();
            SaveSettings(false);
        }

        private void CityComboBox_SelectionChanged_Update()
        {
            CityInfo city = CityComboBox.SelectedItem as CityInfo;
            if (city != null)
            {
                _selectedCity = city.Name;
                _latitude = city.Latitude;
                _longitude = city.Longitude;
                _hasLocation = true;
            }
        }

        private void AutoThemeSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SaveSettings(true);
        }

        private void TimePicker_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
        {
            if (_loading) return;
            SaveSettings(false);
        }

        private void SaveSettings(bool fromToggle)
        {
            if (_loading) return;

            string mode = GetSelectedMode();
            TimeSpan day = DayStartPicker.Time;
            TimeSpan night = NightStartPicker.Time;

            if (mode == "fixed" && day == night)
            {
                ShowHint(InfoBarSeverity.Warning, "无法保存",
                    "「日间开始」和「夜间开始」不能是同一个时间，请修改后再保存。");
                return;
            }

            double latitude = _latitude;
            double longitude = _longitude;
            bool hasLocation = _hasLocation;
            string city = string.Empty;

            if (mode == "city")
            {
                CityInfo selected = CityComboBox.SelectedItem as CityInfo;
                if (selected != null)
                {
                    latitude = selected.Latitude;
                    longitude = selected.Longitude;
                    hasLocation = true;
                    city = selected.Name;
                }
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(AutoValueName, AutoThemeSwitch.IsOn ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue(ModeValueName, mode, RegistryValueKind.String);
                        key.SetValue(DayValueName, FormatTime(day), RegistryValueKind.String);
                        key.SetValue(NightValueName, FormatTime(night), RegistryValueKind.String);

                        if ((mode == "sunrise" || mode == "city") && hasLocation)
                        {
                            key.SetValue(LatValueName, latitude.ToString("0.000000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                            key.SetValue(LonValueName, longitude.ToString("0.000000", CultureInfo.InvariantCulture), RegistryValueKind.String);
                        }

                        if (mode == "city" && !string.IsNullOrEmpty(city))
                        {
                            key.SetValue(CityValueName, city, RegistryValueKind.String);
                        }
                        else
                        {
                            key.DeleteValue(CityValueName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowHint(InfoBarSeverity.Error, "保存失败", ex.Message);
                return;
            }

            bool auto = AutoThemeSwitch.IsOn;
            StatusText.Text = "已自动保存（" + DateTime.Now.ToString("HH:mm:ss") + "）" +
                              (auto
                                  ? "，当前 Windows 主题：" + (IsSystemDark() ? "深色" : "浅色") + "，约 10~15 秒内生效。"
                                  : "；开启自动切换后生效。");

            if (auto && (mode == "sunrise" || mode == "city") && !hasLocation)
            {
                ShowHint(InfoBarSeverity.Warning, "尚未设置位置",
                    "请点击「获取当前位置并计算日出日落」或手动选择城市，否则将回退到自定义时间。");
                return;
            }

            if (fromToggle)
            {
                if (auto)
                {
                    string conflict = DetectThemeConflict();
                    if (conflict != null)
                    {
                        ShowHint(InfoBarSeverity.Warning, "检测到其他主题切换工具",
                            "发现 " + conflict + " 正在运行，两者可能互相覆盖主题设置，建议只保留一个。");
                        return;
                    }
                    ShowHint(InfoBarSeverity.Success, "已开启自动切换主题",
                        "设置已自动保存，约 10~15 秒内按新设置生效。");
                }
                else
                {
                    ShowHint(InfoBarSeverity.Informational, "已关闭自动切换主题",
                        "设置已保存，当前主题保持不变。");
                }
            }
        }

        private static string DetectThemeConflict()
        {
            string[] names = new string[]
            {
                "AutoDarkModeSvc", "AutoDarkModeApp", "AutoDarkModeShell", "PowerToys", "PowerToys.LightSwitch"
            };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(names[i]);
                    if (processes != null && processes.Length > 0)
                    {
                        for (int j = 0; j < processes.Length; j++)
                        {
                            try { processes[j].Dispose(); }
                            catch { }
                        }
                        return names[i];
                    }
                }
                catch
                {
                }
            }
            return null;
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
