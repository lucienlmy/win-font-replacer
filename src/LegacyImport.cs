using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Microsoft.Win32;

namespace FontSwap
{
    /// <summary>
    /// 旧版 WinFontReplacer（1.x）的基线备份：只取当前用户 WindowMetrics 的六项。
    /// 旧版 1.0.3 的问题是把 lfHeight 一起改写（按 DPI 推算），
    /// 因此这里必须把“旧版第一次替换之前”的完整 92 字节取回来，
    /// 新版的一键回退才能回到真正的原始字号。
    /// </summary>
    internal sealed class LegacyBackup
    {
        public string FilePath = string.Empty;
        public string TargetFont = string.Empty;
        public DateTime CreatedUtc = DateTime.MinValue;
        public Dictionary<string, MetricValue> Values = new Dictionary<string, MetricValue>(StringComparer.OrdinalIgnoreCase);

        public bool IsComplete
        {
            get
            {
                foreach (string name in MetricCatalog.All)
                {
                    if (!Values.ContainsKey(name))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }

    internal static class LegacyImporter
    {
        private const string BackupFileName = "baseline.xml";
        private const int LogFontSize = 92;

        /// <summary>旧版可能保存备份的目录，按旧版自己的查找顺序排列。</summary>
        public static List<string> CandidateDirectories()
        {
            List<string> directories = new List<string>();
            AddIfNotEmpty(directories, SafeCombine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinFontReplacer"));
            AddIfNotEmpty(directories, SafeCombine(AppDomain.CurrentDomain.BaseDirectory, "WinFontReplacerData"));
            AddIfNotEmpty(directories, SafeCombine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinFontReplacer"));
            AddIfNotEmpty(directories, SafeCombine(Path.GetTempPath(), "WinFontReplacer"));
            return directories;
        }

        /// <summary>返回所有真实存在的旧版备份文件。</summary>
        public static List<string> FindCandidates()
        {
            List<string> files = new List<string>();
            foreach (string directory in CandidateDirectories())
            {
                string path = SafeCombine(directory, BackupFileName);
                if (path.Length == 0)
                {
                    continue;
                }

                try
                {
                    if (File.Exists(path))
                    {
                        files.Add(path);
                    }
                }
                catch
                {
                }
            }
            return files;
        }

        /// <summary>读取旧版 baseline.xml。格式不对会抛异常，调用方决定是否继续。</summary>
        public static LegacyBackup Load(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("找不到旧版备份文件。", filePath);
            }

            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;

            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            using (XmlReader reader = XmlReader.Create(filePath, settings))
            {
                document.Load(reader);
            }

            XmlElement root = document.DocumentElement;
            if (root == null || !string.Equals(root.LocalName, "fontBackup", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("不是 WinFontReplacer 的备份文件。");
            }

            LegacyBackup backup = new LegacyBackup();
            backup.FilePath = filePath;
            backup.TargetFont = root.GetAttribute("target");

            DateTime created;
            if (DateTime.TryParse(root.GetAttribute("createdUtc"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out created))
            {
                backup.CreatedUtc = created;
            }

            XmlNodeList nodes = root.SelectNodes("value");
            if (nodes == null)
            {
                return backup;
            }

            foreach (XmlNode node in nodes)
            {
                if (node == null || node.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                XmlElement element = (XmlElement)node;
                if (!string.Equals(element.GetAttribute("root"), "CurrentUser", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = element.GetAttribute("name");
                if (!IsMetric(name) || backup.Values.ContainsKey(name))
                {
                    continue;
                }

                MetricValue value = ReadValue(element, name);
                if (value != null)
                {
                    backup.Values[name] = value;
                }
            }

            return backup;
        }

        /// <summary>把旧版基线转成新版回退备份。已有备份时不覆盖。</summary>
        public static bool ImportFrom(string filePath, out string message)
        {
            message = string.Empty;
            try
            {
                if (BackupStore.HasBackup())
                {
                    message = "当前已经有一份回退备份，未覆盖。";
                    return false;
                }

                LegacyBackup backup = Load(filePath);
                if (!backup.IsComplete)
                {
                    message = "旧版备份里缺少窗口字体数据，已跳过。";
                    return false;
                }

                BackupData data = new BackupData();
                data.AppliedFont = MetricReader.CurrentInterfaceFont();
                data.CapturedAt = DateTime.Now;
                foreach (string name in MetricCatalog.All)
                {
                    data.AppliedMetrics.Add(name);
                    data.Values[name] = backup.Values[name];
                }

                BackupStore.Save(data);
                OperationLog.Write("已导入旧版 WinFontReplacer 备份：" + filePath);
                message = "已接管旧版 WinFontReplacer 的备份，可回退到安装前的字体（" + DescribeOriginal(data) + "）。";
                return true;
            }
            catch (Exception ex)
            {
                OperationLog.Write("导入旧版备份失败：" + filePath + "，原因：" + ex);
                message = "导入旧版备份失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>自动查找并导入第一份可用的旧版备份。</summary>
        public static bool TryImport(out string message)
        {
            message = string.Empty;
            List<string> candidates = FindCandidates();
            if (candidates.Count == 0)
            {
                message = "没有找到旧版备份。";
                return false;
            }

            foreach (string path in candidates)
            {
                string detail;
                if (ImportFrom(path, out detail))
                {
                    message = detail;
                    return true;
                }
            }

            message = "找到旧版备份，但没有可导入的数据。";
            return false;
        }

        private static string DescribeOriginal(BackupData data)
        {
            MetricValue message;
            if (data.Values.TryGetValue("MessageFont", out message) && message != null && message.Data != null)
            {
                string face = LogFontCodec.GetFace(message.Data);
                if (!string.IsNullOrEmpty(face))
                {
                    return face;
                }
            }
            return "未知";
        }

        private static bool IsMetric(string name)
        {
            foreach (string metric in MetricCatalog.All)
            {
                if (string.Equals(metric, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static MetricValue ReadValue(XmlElement element, string name)
        {
            string existsText = element.GetAttribute("exists");
            bool exists = string.IsNullOrEmpty(existsText) ||
                string.Equals(existsText, "true", StringComparison.OrdinalIgnoreCase);
            if (!exists)
            {
                return null;
            }

            string kindText = element.GetAttribute("kind");
            if (!string.Equals(kindText, "Binary", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            XmlNode binary = element.SelectSingleNode("binary");
            if (binary == null)
            {
                return null;
            }

            byte[] data;
            try
            {
                data = Convert.FromBase64String(binary.InnerText.Trim());
            }
            catch
            {
                return null;
            }

            if (data == null || data.Length != LogFontSize)
            {
                return null;
            }

            MetricValue value = new MetricValue();
            value.Name = name;
            value.Exists = true;
            value.Kind = RegistryValueKind.Binary;
            value.Data = data;
            return value;
        }

        private static void AddIfNotEmpty(List<string> list, string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                return;
            }

            if (!list.Contains(item))
            {
                list.Add(item);
            }
        }

        private static string SafeCombine(string directory, string name)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return string.Empty;
            }

            try
            {
                return Path.Combine(directory, name);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
