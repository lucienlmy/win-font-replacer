using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;

namespace FontSwap
{
    internal static class FontCatalog
    {
        private static List<string> _all;

        public static List<string> All
        {
            get
            {
                if (_all == null)
                {
                    _all = Load();
                }
                return _all;
            }
        }

        private static List<string> Load()
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (InstalledFontCollection collection = new InstalledFontCollection())
                {
                    foreach (FontFamily family in collection.Families)
                    {
                        string name = family.Name;
                        if (string.IsNullOrEmpty(name) || name.StartsWith("@", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        if (seen.Add(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch
            {
            }

            names.Sort(StringComparer.Create(CultureInfo.CurrentCulture, true));
            return names;
        }

        public static List<string> Filter(string query)
        {
            List<string> all = All;
            if (string.IsNullOrEmpty(query) || query.Trim().Length == 0)
            {
                return all;
            }

            string needle = query.Trim();
            List<string> result = new List<string>();
            foreach (string name in all)
            {
                if (name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>按名称创建字体，失败时回到界面字体。</summary>
        public static Font CreateFont(string family, float size, FontStyle style, Font fallback)
        {
            try
            {
                return new Font(family, size, style, GraphicsUnit.Point);
            }
            catch
            {
                try
                {
                    return new Font(fallback.FontFamily, size, style, GraphicsUnit.Point);
                }
                catch
                {
                    return (Font)fallback.Clone();
                }
            }
        }

        public static float PointSizeForHeight(int height)
        {
            int magnitude = Math.Abs(height);
            if (magnitude == 0)
            {
                return 9f;
            }
            float points = magnitude * 72f / 96f;
            if (points < 5f)
            {
                points = 5f;
            }
            if (points > 48f)
            {
                points = 48f;
            }
            return points;
        }
    }
}
