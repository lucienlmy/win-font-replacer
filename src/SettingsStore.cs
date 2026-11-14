using System;
using Microsoft.Win32;

namespace FontSwap
{
    internal static class SettingsStore
    {
        private const string SettingsKeyPath = @"Software\WinFontReplacer\Settings";

        public static bool IncludeIconFont
        {
            get { return ReadString("IncludeIconFont") == "1"; }
            set { WriteString("IncludeIconFont", value ? "1" : "0"); }
        }

        public static string LastFont
        {
            get { return ReadString("LastFont"); }
            set { WriteString("LastFont", value ?? string.Empty); }
        }

        private static string ReadString(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, false))
                {
                    if (key == null)
                    {
                        return string.Empty;
                    }
                    string value = key.GetValue(name) as string;
                    return value ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void WriteString(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value ?? string.Empty, RegistryValueKind.String);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
