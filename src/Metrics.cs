using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace FontSwap
{
    /// <summary>WindowMetrics 下的一个字体值，保留原始类型与原始字节。</summary>
    internal sealed class MetricValue
    {
        public string Name;
        public bool Exists;
        public RegistryValueKind Kind;
        public byte[] Data;
        public string Text;
        public object Raw;
    }

    internal sealed class MetricSnapshot
    {
        public Dictionary<string, MetricValue> Values = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

        public MetricValue this[string name]
        {
            get
            {
                MetricValue value;
                return Values.TryGetValue(name, out value) ? value : null;
            }
        }
    }

    internal static class MetricCatalog
    {
        public const string WindowMetricsKey = @"Control Panel\Desktop\WindowMetrics";

        /// <summary>六项传统窗口字体（顺序固定）。</summary>
        public static readonly string[] All = new string[]
        {
            "CaptionFont",
            "SmCaptionFont",
            "MenuFont",
            "StatusFont",
            "MessageFont",
            "IconFont"
        };

        /// <summary>默认替换的五项，不包含桌面图标文字。</summary>
        public static readonly string[] Standard = new string[]
        {
            "CaptionFont",
            "SmCaptionFont",
            "MenuFont",
            "StatusFont",
            "MessageFont"
        };

        public static string DisplayName(string metric)
        {
            switch (metric)
            {
                case "CaptionFont": return "标题栏";
                case "SmCaptionFont": return "小标题栏";
                case "MenuFont": return "菜单";
                case "StatusFont": return "状态栏";
                case "MessageFont": return "对话框";
                case "IconFont": return "图标文字";
                default: return metric;
            }
        }
    }

    internal static class MetricReader
    {
        public static MetricSnapshot Capture()
        {
            MetricSnapshot snapshot = new MetricSnapshot();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MetricCatalog.WindowMetricsKey, false))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的窗口字体设置。");
                }

                foreach (string name in MetricCatalog.All)
                {
                    snapshot.Values[name] = ReadOne(key, name);
                }
            }
            return snapshot;
        }

        public static MetricValue ReadOne(RegistryKey key, string name)
        {
            MetricValue value = new MetricValue();
            value.Name = name;

            try
            {
                object raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (raw == null)
                {
                    value.Exists = false;
                    value.Kind = RegistryValueKind.None;
                    return value;
                }

                value.Exists = true;
                value.Raw = raw;
                value.Kind = key.GetValueKind(name);

                if (value.Kind == RegistryValueKind.Binary)
                {
                    value.Data = (byte[])raw;
                }
                else if (value.Kind == RegistryValueKind.String)
                {
                    value.Text = raw as string;
                    value.Data = HexText.Parse(value.Text);
                    if (value.Data.Length != LogFontCodec.Size)
                    {
                        value.Data = null;
                    }
                }
                else
                {
                    value.Data = null;
                }
            }
            catch (Exception ex)
            {
                if (!(ex is SecurityException) && !(ex is IOException) && !(ex is UnauthorizedAccessException))
                {
                    throw;
                }
                throw new InvalidOperationException("读取窗口字体值 " + name + " 失败：" + ex.Message, ex);
            }

            return value;
        }

        /// <summary>读取系统当前界面字体名（以菜单字体为准）。</summary>
        public static string CurrentInterfaceFont()
        {
            try
            {
                MetricSnapshot snapshot = Capture();
                MetricValue menu = snapshot["MenuFont"];
                if (menu != null && menu.Data != null)
                {
                    string face = LogFontCodec.GetFace(menu.Data);
                    if (!string.IsNullOrEmpty(face))
                    {
                        return face;
                    }
                }
            }
            catch
            {
            }

            try
            {
                byte[] data = LogFontCodec.DefaultFor("MenuFont");
                return LogFontCodec.GetFace(data);
            }
            catch
            {
                return "（未知）";
            }
        }

        public static int MenuFontHeight()
        {
            try
            {
                MetricSnapshot snapshot = Capture();
                MetricValue menu = snapshot["MenuFont"];
                if (menu != null && menu.Data != null)
                {
                    return LogFontCodec.Height(menu.Data);
                }
            }
            catch
            {
            }
            return -12;
        }

        public static string CurrentIconFont()
        {
            try
            {
                MetricSnapshot snapshot = Capture();
                MetricValue icon = snapshot["IconFont"];
                if (icon != null && icon.Exists && icon.Data != null)
                {
                    return LogFontCodec.GetFace(icon.Data);
                }
            }
            catch
            {
            }
            return string.Empty;
        }
    }

    internal sealed class BackupData
    {
        public string AppliedFont = string.Empty;
        public DateTime CapturedAt = DateTime.MinValue;
        public List<string> AppliedMetrics = new List<string>();
        public Dictionary<string, MetricValue> Values = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class BackupStore
    {
        private const string BackupKeyPath = @"Software\WinFontReplacer\Backup";
        private const string BackupFileName = "backup.txt";

        public static bool HasBackup()
        {
            return ReadRegistryBackup() != null || File.Exists(LocalBackupPath);
        }

        public static BackupData Load()
        {
            BackupData data = ReadRegistryBackup();
            if (data != null)
            {
                return data;
            }

            data = ReadLocalBackup();
            if (data != null)
            {
                return data;
            }
            return null;
        }

        public static string AppliedFont()
        {
            BackupData data = Load();
            return data == null ? string.Empty : data.AppliedFont;
        }

        public static void Save(BackupData data)
        {
            try
            {
                WriteRegistryBackup(data);
            }
            catch
            {
                // 本地文件是第二份保险，注册表失败时不阻断，但必须保证本地写入成功。
            }

            WriteLocalBackup(data);
        }

        public static void UpdateAppliedFont(string font, List<string> metrics)
        {
            // 记录“曾经替换过的所有项目”而不是最后一次的项目，避免信息丢失。
            List<string> merged = new List<string>();
            BackupData existing = Load();
            foreach (string name in MetricCatalog.All)
            {
                if (ContainsName(metrics, name) || (existing != null && ContainsName(existing.AppliedMetrics, name)))
                {
                    merged.Add(name);
                }
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(BackupKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("State", "Applied", RegistryValueKind.String);
                        key.SetValue("AppliedFont", font ?? string.Empty, RegistryValueKind.String);
                        key.SetValue("AppliedMetrics", string.Join(",", merged.ToArray()), RegistryValueKind.String);
                    }
                }
            }
            catch
            {
            }

            try
            {
                BackupData data = Load();
                if (data != null)
                {
                    data.AppliedFont = font ?? string.Empty;
                    data.AppliedMetrics = merged;
                    WriteLocalBackup(data);
                }
            }
            catch
            {
            }
        }

        private static bool ContainsName(List<string> names, string name)
        {
            if (names == null)
            {
                return false;
            }
            foreach (string item in names)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static void Delete()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(BackupKeyPath, false);
            }
            catch
            {
            }

            try
            {
                if (File.Exists(LocalBackupPath))
                {
                    File.Delete(LocalBackupPath);
                }
            }
            catch
            {
            }
        }

        public static string LocalBackupPath
        {
            get { return Path.Combine(AppPaths.DataDirectory, BackupFileName); }
        }

        private static void WriteRegistryBackup(BackupData data)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(BackupKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法创建字体备份。");
                }

                key.SetValue("State", "Applied", RegistryValueKind.String);
                key.SetValue("AppliedFont", data.AppliedFont ?? string.Empty, RegistryValueKind.String);
                key.SetValue("CapturedAt", data.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"), RegistryValueKind.String);
                key.SetValue("AppliedMetrics", string.Join(",", data.AppliedMetrics.ToArray()), RegistryValueKind.String);

                using (RegistryKey original = key.CreateSubKey("Original"))
                {
                    if (original == null)
                    {
                        throw new InvalidOperationException("无法创建字体备份子项。");
                    }

                    foreach (string name in MetricCatalog.All)
                    {
                        MetricValue value;
                        if (!data.Values.TryGetValue(name, out value))
                        {
                            continue;
                        }

                        string kindText = "Missing";
                        if (value.Exists)
                        {
                            if (value.Kind == RegistryValueKind.Binary)
                            {
                                kindText = "Binary";
                            }
                            else if (value.Kind == RegistryValueKind.String)
                            {
                                kindText = "String";
                            }
                            else
                            {
                                throw new InvalidOperationException("不支持备份的值类型：" + value.Kind);
                            }
                        }

                        original.SetValue(name, kindText, RegistryValueKind.String);
                        if (!value.Exists)
                        {
                            continue;
                        }

                        if (value.Kind == RegistryValueKind.Binary)
                        {
                            original.SetValue(name + ".Data", value.Data, RegistryValueKind.Binary);
                        }
                        else
                        {
                            original.SetValue(name + ".Data", value.Text ?? HexText.Format(value.Data), RegistryValueKind.String);
                        }
                    }
                }
            }
        }

        private static BackupData ReadRegistryBackup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(BackupKeyPath, false))
                {
                    if (key == null)
                    {
                        return null;
                    }

                    object state = key.GetValue("State");
                    if (!(state is string) || !string.Equals((string)state, "Applied", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    BackupData data = new BackupData();
                    data.AppliedFont = key.GetValue("AppliedFont") as string ?? string.Empty;
                    string captured = key.GetValue("CapturedAt") as string;
                    DateTime parsed;
                    if (!string.IsNullOrEmpty(captured) &&
                        DateTime.TryParseExact(captured, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    {
                        data.CapturedAt = parsed;
                    }

                    string applied = key.GetValue("AppliedMetrics") as string;
                    if (!string.IsNullOrEmpty(applied))
                    {
                        data.AppliedMetrics.AddRange(applied.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                    }

                    using (RegistryKey original = key.OpenSubKey("Original", false))
                    {
                        if (original == null)
                        {
                            return null;
                        }

                        foreach (string name in MetricCatalog.All)
                        {
                            string kindText = original.GetValue(name) as string;
                            MetricValue value = new MetricValue();
                            value.Name = name;

                            if (string.IsNullOrEmpty(kindText) || string.Equals(kindText, "Missing", StringComparison.OrdinalIgnoreCase))
                            {
                                value.Exists = false;
                                value.Kind = RegistryValueKind.None;
                                data.Values[name] = value;
                                continue;
                            }

                            if (string.Equals(kindText, "Binary", StringComparison.OrdinalIgnoreCase))
                            {
                                byte[] bytes = original.GetValue(name + ".Data") as byte[];
                                if (bytes == null)
                                {
                                    return null;
                                }
                                value.Exists = true;
                                value.Kind = RegistryValueKind.Binary;
                                value.Data = bytes;
                                value.Raw = bytes;
                            }
                            else
                            {
                                string text = original.GetValue(name + ".Data") as string;
                                if (text == null)
                                {
                                    return null;
                                }
                                value.Exists = true;
                                value.Kind = RegistryValueKind.String;
                                value.Text = text;
                                value.Data = HexText.Parse(text);
                                value.Raw = text;
                            }

                            data.Values[name] = value;
                        }
                    }

                    if (data.AppliedMetrics.Count == 0)
                    {
                        data.AppliedMetrics.AddRange(MetricCatalog.All);
                    }

                    if (data.Values.Count != MetricCatalog.All.Length)
                    {
                        return null;
                    }
                    return data;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void WriteLocalBackup(BackupData data)
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("format=1");
            builder.AppendLine("appliedFont=" + (data.AppliedFont ?? string.Empty));
            builder.AppendLine("capturedAt=" + data.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("metrics=" + string.Join(",", data.AppliedMetrics.ToArray()));

            foreach (string name in MetricCatalog.All)
            {
                MetricValue value;
                if (!data.Values.TryGetValue(name, out value))
                {
                    continue;
                }

                builder.AppendLine("[" + name + "]");
                if (!value.Exists)
                {
                    builder.AppendLine("kind=Missing");
                    continue;
                }

                if (value.Kind == RegistryValueKind.Binary)
                {
                    builder.AppendLine("kind=Binary");
                    builder.AppendLine("data=" + HexText.Format(value.Data));
                }
                else
                {
                    builder.AppendLine("kind=String");
                    builder.AppendLine("data=" + (value.Text ?? HexText.Format(value.Data)));
                }
            }

            File.WriteAllText(LocalBackupPath, builder.ToString(), new UTF8Encoding(false));
        }

        private static BackupData ReadLocalBackup()
        {
            try
            {
                if (!File.Exists(LocalBackupPath))
                {
                    return null;
                }

                string[] lines = File.ReadAllLines(LocalBackupPath, Encoding.UTF8);
                BackupData data = new BackupData();
                string current = null;
                string currentKind = null;
                string currentData = null;

                Action<string, string, string> commit = delegate(string name, string kind, string payload)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return;
                    }

                    MetricValue value = new MetricValue();
                    value.Name = name;
                    if (string.Equals(kind, "Missing", StringComparison.OrdinalIgnoreCase))
                    {
                        value.Exists = false;
                        value.Kind = RegistryValueKind.None;
                    }
                    else if (string.Equals(kind, "String", StringComparison.OrdinalIgnoreCase))
                    {
                        value.Exists = true;
                        value.Kind = RegistryValueKind.String;
                        value.Text = payload;
                        value.Data = HexText.Parse(payload);
                        value.Raw = payload;
                    }
                    else
                    {
                        value.Exists = true;
                        value.Kind = RegistryValueKind.Binary;
                        value.Data = HexText.Parse(payload);
                        value.Raw = value.Data;
                    }
                    data.Values[name] = value;
                };

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line.StartsWith("format=", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("capturedAt=", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("metrics=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (line.StartsWith("metrics=", StringComparison.OrdinalIgnoreCase))
                        {
                            data.AppliedMetrics.AddRange(line.Substring(8).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                        }
                        continue;
                    }

                    if (line.StartsWith("appliedFont=", StringComparison.OrdinalIgnoreCase))
                    {
                        data.AppliedFont = line.Substring(12);
                        continue;
                    }

                    if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                    {
                        if (current != null)
                        {
                            commit(current, currentKind, currentData);
                        }
                        current = line.Substring(1, line.Length - 2);
                        currentKind = null;
                        currentData = null;
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0 || current == null)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq);
                    string value = line.Substring(eq + 1);
                    if (string.Equals(key, "kind", StringComparison.OrdinalIgnoreCase))
                    {
                        currentKind = value;
                    }
                    else if (string.Equals(key, "data", StringComparison.OrdinalIgnoreCase))
                    {
                        currentData = value;
                    }
                }

                if (current != null)
                {
                    commit(current, currentKind, currentData);
                }

                if (data.AppliedMetrics.Count == 0)
                {
                    data.AppliedMetrics.AddRange(MetricCatalog.All);
                }

                if (data.Values.Count != MetricCatalog.All.Length)
                {
                    return null;
                }
                return data;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class AppPaths
    {
        private static string _dataDirectory;

        public static string DataDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_dataDirectory))
                {
                    return _dataDirectory;
                }

                // 沿用 1.x 的目录规则：老用户直接覆盖升级时，新版能接着用原来的数据目录。
                string candidate = Path.Combine(CommonApplicationData(), "WinFontReplacer");
                if (File.Exists(Path.Combine(candidate, "baseline.xml")))
                {
                    _dataDirectory = candidate;
                    return _dataDirectory;
                }

                candidate = Path.Combine(ApplicationDirectory(), "WinFontReplacerData");
                if (TryPrepare(candidate))
                {
                    _dataDirectory = candidate;
                    return _dataDirectory;
                }

                candidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WinFontReplacer");
                if (TryPrepare(candidate))
                {
                    _dataDirectory = candidate;
                    return _dataDirectory;
                }

                candidate = Path.Combine(CommonApplicationData(), "WinFontReplacer");
                if (TryPrepare(candidate))
                {
                    _dataDirectory = candidate;
                    return _dataDirectory;
                }

                candidate = Path.Combine(Path.GetTempPath(), "WinFontReplacer");
                Directory.CreateDirectory(candidate);
                _dataDirectory = candidate;
                return _dataDirectory;
            }
        }

        public static string LogDirectory
        {
            get { return Path.Combine(DataDirectory, "logs"); }
        }

        private static string CommonApplicationData()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        }

        private static string ApplicationDirectory()
        {
            try
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                return Path.GetTempPath();
            }
        }

        private static bool TryPrepare(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class OperationLog
    {
        private static readonly object Sync = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(AppPaths.LogDirectory);
                    string path = Path.Combine(
                        AppPaths.LogDirectory,
                        "winfontreplacer-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(
                        path,
                        DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }
}
