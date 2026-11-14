using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using Microsoft.Win32;

namespace FontSwap
{
    internal sealed class ApplyResult
    {
        public string FontFamily = string.Empty;
        public bool IconIncluded;
        public List<string> Changed = new List<string>();
        public string Note = string.Empty;
    }

    internal sealed class FontOperations
    {
        private const string MutexName = "Global\\WinFontReplacer.Operation";

        /// <summary>把界面字体族替换为 fontFamily，字号与其他所有字段保持原样。</summary>
        public static ApplyResult Apply(string fontFamily, bool includeIconFont)
        {
            string face = NormalizeFace(fontFamily);
            if (face.Length == 0)
            {
                throw new ArgumentException("请先选择一个字体。");
            }

            using (Mutex mutex = AcquireMutex())
            {
                MetricSnapshot before = MetricReader.Capture();
                string[] targets = includeIconFont ? MetricCatalog.All : MetricCatalog.Standard;

                List<string> changed = new List<string>();
                bool backupExisted = BackupStore.HasBackup();

                if (!backupExisted)
                {
                    BackupData backup = new BackupData();
                    backup.AppliedFont = face;
                    backup.CapturedAt = DateTime.Now;
                    backup.AppliedMetrics = new List<string>(targets);
                    foreach (string name in MetricCatalog.All)
                    {
                        backup.Values[name] = before.Values[name];
                    }
                    BackupStore.Save(backup);
                }
                else
                {
                    BackupStore.UpdateAppliedFont(face, new List<string>(targets));
                }

                try
                {
                    foreach (string name in targets)
                    {
                        MetricValue current = before.Values[name];
                        byte[] source = ResolveSource(name, current);
                        byte[] updated = LogFontCodec.WithFace(source, face);
                        WriteMetric(name, updated, current);
                        changed.Add(name);
                    }

                    VerifyApplied(before, targets, face);

                    Broadcast();
                    OperationLog.Write("应用字体：" + face + "；项目：" + string.Join(",", targets)
                        + "；备份" + (backupExisted ? "沿用首次快照" : "已创建"));

                    ApplyResult result = new ApplyResult();
                    result.FontFamily = face;
                    result.IconIncluded = includeIconFont;
                    result.Changed.AddRange(changed);
                    if (!backupExisted)
                    {
                        result.Note = "已保存原始字体，可随时一键回退。";
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    try
                    {
                        RestoreSnapshot(before, MetricCatalog.All);
                        Broadcast();
                    }
                    catch (Exception rollbackError)
                    {
                        OperationLog.Write("自动回滚失败：" + rollbackError);
                    }

                    if (!backupExisted)
                    {
                        BackupStore.Delete();
                    }

                    OperationLog.Write("应用失败：" + ex);
                    throw new InvalidOperationException("应用字体失败，已恢复改动：" + ex.Message, ex);
                }
            }
        }

        /// <summary>把六项字体恢复到首次应用前的原始状态，并清除备份。</summary>
        public static ApplyResult Restore()
        {
            using (Mutex mutex = AcquireMutex())
            {
                BackupData backup = BackupStore.Load();
                if (backup == null)
                {
                    throw new InvalidOperationException("没有找到可回退的原始字体备份。");
                }

                MetricSnapshot before = MetricReader.Capture();

                // 回退时恢复备份中存在的全部项目：多次应用可能先后改动不同的项
                // （例如第一次含图标、第二次不含），只按最近一次的项目恢复会留下残留。
                List<string> targets = new List<string>();
                foreach (string name in MetricCatalog.All)
                {
                    MetricValue original;
                    if (backup.Values.TryGetValue(name, out original))
                    {
                        targets.Add(name);
                    }
                }
                if (targets.Count == 0)
                {
                    throw new InvalidOperationException("备份中没有可恢复的字体数据。");
                }

                try
                {
                    foreach (string name in targets)
                    {
                        MetricValue original;
                        if (!backup.Values.TryGetValue(name, out original))
                        {
                            continue;
                        }
                        WriteOriginal(name, original);
                    }

                    VerifyRestored(backup, targets);
                    BackupStore.Delete();
                    Broadcast();
                    OperationLog.Write("回退字体至原始状态；项目：" + string.Join(",", targets));

                    ApplyResult result = new ApplyResult();
                    result.FontFamily = backup.AppliedFont;
                    result.Changed.AddRange(targets);
                    return result;
                }
                catch (Exception ex)
                {
                    try
                    {
                        RestoreSnapshot(before, MetricCatalog.All);
                        Broadcast();
                    }
                    catch (Exception rollbackError)
                    {
                        OperationLog.Write("回退失败后的二次恢复失败：" + rollbackError);
                    }

                    OperationLog.Write("回退失败：" + ex);
                    throw new InvalidOperationException("回退字体失败，已恢复操作前的状态：" + ex.Message, ex);
                }
            }
        }

        public static bool HasBackup()
        {
            return BackupStore.HasBackup();
        }

        /// <summary>本次替换时使用的字体名（即备份记录中的应用字体）。</summary>
        public static string BackupFont()
        {
            return BackupStore.AppliedFont();
        }

        /// <summary>备份中保存的原始界面字体名；从未应用过时为空。</summary>
        public static string OriginalFont()
        {
            BackupData data = BackupStore.Load();
            if (data == null || data.Values == null)
            {
                return string.Empty;
            }

            string[] order = new string[] { "CaptionFont", "MenuFont", "MessageFont", "StatusFont", "SmCaptionFont" };
            foreach (string name in order)
            {
                MetricValue value;
                if (data.Values.TryGetValue(name, out value) && value != null && value.Data != null &&
                    value.Data.Length == LogFontCodec.Size)
                {
                    return LogFontCodec.GetFace(value.Data);
                }
            }
            return string.Empty;
        }

        public static void RestartExplorer()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/f /im explorer.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                });
            }
            catch (Exception ex)
            {
                OperationLog.Write("结束资源管理器失败：" + ex);
            }

            Thread.Sleep(900);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                OperationLog.Write("启动资源管理器失败：" + ex);
                throw new InvalidOperationException("无法重新启动资源管理器，请手动运行 explorer.exe。", ex);
            }
        }

        private static string NormalizeFace(string fontFamily)
        {
            if (fontFamily == null)
            {
                return string.Empty;
            }

            string face = fontFamily.Trim();
            if (face.Length > LogFontCodec.MaxFaceLength)
            {
                face = face.Substring(0, LogFontCodec.MaxFaceLength);
            }
            return face;
        }

        private static byte[] ResolveSource(string name, MetricValue current)
        {
            if (current != null && current.Exists && current.Data != null && current.Data.Length == LogFontCodec.Size)
            {
                return current.Data;
            }

            if (current != null && current.Exists && (current.Kind == RegistryValueKind.DWord
                || current.Kind == RegistryValueKind.MultiString
                || current.Kind == RegistryValueKind.ExpandString
                || current.Kind == RegistryValueKind.QWord
                || current.Kind == RegistryValueKind.Unknown))
            {
                throw new InvalidOperationException(
                    "窗口字体值 " + name + " 的类型不受支持（" + current.Kind + "）。");
            }

            OperationLog.Write("值 " + name + " 缺失或长度异常，使用系统默认模板补齐。");
            return LogFontCodec.DefaultFor(name);
        }

        private static void WriteMetric(string name, byte[] data, MetricValue current)
        {
            using (RegistryKey key = OpenMetricsKey(true))
            {
                if (current == null || !current.Exists || current.Kind == RegistryValueKind.None)
                {
                    key.SetValue(name, data, RegistryValueKind.Binary);
                    return;
                }

                if (current.Kind == RegistryValueKind.Binary)
                {
                    key.SetValue(name, data, RegistryValueKind.Binary);
                }
                else if (current.Kind == RegistryValueKind.String)
                {
                    key.SetValue(name, HexText.Format(data), RegistryValueKind.String);
                }
                else
                {
                    throw new InvalidOperationException(
                        "不支持写入的值类型：" + current.Kind + "（" + name + "）。");
                }
            }
        }

        private static void WriteOriginal(string name, MetricValue original)
        {
            using (RegistryKey key = OpenMetricsKey(true))
            {
                if (original == null || !original.Exists)
                {
                    key.DeleteValue(name, false);
                    return;
                }

                if (original.Kind == RegistryValueKind.Binary)
                {
                    key.SetValue(name, original.Data, RegistryValueKind.Binary);
                }
                else if (original.Kind == RegistryValueKind.String)
                {
                    key.SetValue(name, original.Text ?? HexText.Format(original.Data), RegistryValueKind.String);
                }
                else
                {
                    throw new InvalidOperationException(
                        "备份中包含不支持恢复的值类型：" + original.Kind + "（" + name + "）。");
                }
            }
        }

        private static void RestoreSnapshot(MetricSnapshot snapshot, string[] names)
        {
            foreach (string name in names)
            {
                MetricValue value = snapshot[name];
                if (value == null)
                {
                    continue;
                }
                WriteOriginal(name, value);
            }
        }

        private static void VerifyApplied(MetricSnapshot before, string[] targets, string face)
        {
            MetricSnapshot after = MetricReader.Capture();

            foreach (string name in targets)
            {
                MetricValue previous = before[name];
                MetricValue current = after[name];

                if (current == null || !current.Exists || current.Data == null)
                {
                    throw new InvalidOperationException("写入后读不到 " + name + " 的字体数据。");
                }

                string actualFace = LogFontCodec.GetFace(current.Data);
                if (!string.Equals(actualFace, face, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        name + " 的字体名写入后为 \"" + actualFace + "\"，与预期不符。");
                }

                if (previous != null && previous.Exists && previous.Data != null && previous.Data.Length == LogFontCodec.Size)
                {
                    if (!LogFontCodec.SameExceptFace(previous.Data, current.Data))
                    {
                        throw new InvalidOperationException(
                            name + " 在替换字体时改动了字号或其他字段，已中止。");
                    }
                }
            }
        }

        private static void VerifyRestored(BackupData backup, List<string> targets)
        {
            MetricSnapshot after = MetricReader.Capture();
            foreach (string name in targets)
            {
                MetricValue original;
                if (!backup.Values.TryGetValue(name, out original))
                {
                    continue;
                }

                MetricValue current = after[name];
                if (original.Exists)
                {
                    if (current == null || !current.Exists || current.Data == null)
                    {
                        throw new InvalidOperationException(name + " 未能恢复为原始值。");
                    }
                    if (original.Data != null && !BytesEqual(original.Data, current.Data))
                    {
                        throw new InvalidOperationException(name + " 恢复后的字节与备份不一致。");
                    }
                }
                else if (current != null && current.Exists)
                {
                    throw new InvalidOperationException(name + " 原本不存在，但恢复后仍然存在。");
                }
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static RegistryKey OpenMetricsKey(bool writable)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(MetricCatalog.WindowMetricsKey, writable);
            if (key == null)
            {
                throw new InvalidOperationException("无法打开当前用户的窗口字体设置。");
            }
            return key;
        }

        private static Mutex AcquireMutex()
        {
            Mutex mutex = new Mutex(false, MutexName);
            bool acquired = false;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(5000);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                {
                    mutex.Dispose();
                    throw new InvalidOperationException("另一个字体操作仍在进行，请稍后再试。");
                }
                return mutex;
            }
            catch
            {
                if (!acquired)
                {
                    mutex.Dispose();
                }
                throw;
            }
        }

        private static void Broadcast()
        {
            NativeMethods.BroadcastSettingChange("WindowMetrics");
            NativeMethods.BroadcastSettingChange("Fonts");
        }
    }
}
