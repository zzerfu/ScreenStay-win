using System;

namespace KeepAwake.Shared
{
    /// <summary>
    /// 日出 / 日落近似计算（NOAA 简化算法），返回本地时间。
    /// 精度约 1~2 分钟，对自动切换主题足够。
    /// </summary>
    internal static class SunCalculator
    {
        internal static bool TryCompute(DateTime localDate, double latitude, double longitude,
            out TimeSpan sunrise, out TimeSpan sunset)
        {
            sunrise = TimeSpan.Zero;
            sunset = TimeSpan.Zero;
            try
            {
                if (double.IsNaN(latitude) || double.IsNaN(longitude)) return false;
                if (latitude < -89.9 || latitude > 89.9) return false;
                if (longitude < -180.0 || longitude > 180.0) return false;

                int dayOfYear = localDate.DayOfYear;
                double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1 + 0.5);

                double eqTime = 229.18 * (0.000075
                    + 0.001868 * Math.Cos(gamma)
                    - 0.032077 * Math.Sin(gamma)
                    - 0.014615 * Math.Cos(2.0 * gamma)
                    - 0.040849 * Math.Sin(2.0 * gamma));

                double declination = 0.006918
                    - 0.399912 * Math.Cos(gamma)
                    + 0.070257 * Math.Sin(gamma)
                    - 0.006758 * Math.Cos(2.0 * gamma)
                    + 0.000907 * Math.Sin(2.0 * gamma)
                    - 0.002697 * Math.Cos(3.0 * gamma)
                    + 0.00148 * Math.Sin(3.0 * gamma);

                double latRad = latitude * Math.PI / 180.0;
                double cosHa = Math.Cos(90.833 * Math.PI / 180.0) / (Math.Cos(latRad) * Math.Cos(declination))
                    - Math.Tan(latRad) * Math.Tan(declination);

                // 极昼 / 极夜时无法计算，调用方回退到固定时间
                if (cosHa > 1.0 || cosHa < -1.0) return false;

                double ha = Math.Acos(cosHa) * 180.0 / Math.PI;
                double sunriseUtcMinutes = 720.0 - 4.0 * (longitude + ha) - eqTime;
                double sunsetUtcMinutes = 720.0 - 4.0 * (longitude - ha) - eqTime;

                TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(localDate.Date.AddHours(12.0));
                sunrise = NormalizeMinutes(sunriseUtcMinutes + offset.TotalMinutes);
                sunset = NormalizeMinutes(sunsetUtcMinutes + offset.TotalMinutes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static TimeSpan NormalizeMinutes(double minutes)
        {
            double value = minutes % 1440.0;
            if (value < 0.0) value += 1440.0;
            return TimeSpan.FromMinutes(Math.Round(value));
        }
    }
}
