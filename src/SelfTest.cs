using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FontSwap
{
    internal static class SelfTest
    {
        public static int Run()
        {
            StringBuilder report = new StringBuilder();
            int failures = 0;

            Action<string, bool, string> check = delegate(string name, bool ok, string detail)
            {
                if (!ok)
                {
                    failures++;
                }
                report.AppendLine((ok ? "PASS  " : "FAIL  ") + name +
                    (string.IsNullOrEmpty(detail) ? string.Empty : "  ->  " + detail));
            };

            check("LOGFONT 大小为 92 字节", LogFontCodec.Size == 92, LogFontCodec.Size.ToString());

            byte[] sample = new byte[92];
            BitConverter.GetBytes(-18).CopyTo(sample, 0);
            BitConverter.GetBytes(400).CopyTo(sample, 16);
            sample[23] = 1;
            sample[26] = 5;
            byte[] faceBytes = Encoding.Unicode.GetBytes("TestFace");
            Array.Copy(faceBytes, 0, sample, LogFontCodec.FaceOffset, faceBytes.Length);

            byte[] replaced = LogFontCodec.WithFace(sample, "微软雅黑");
            check("替换字体名后其他字节不变", LogFontCodec.SameExceptFace(sample, replaced), string.Empty);
            check("字体名写入正确", string.Equals(LogFontCodec.GetFace(replaced), "微软雅黑", StringComparison.Ordinal),
                LogFontCodec.GetFace(replaced));
            check("替换后字号保持 -18", LogFontCodec.Height(replaced) == -18, LogFontCodec.Height(replaced).ToString());

            byte[] roundTrip = HexText.Parse(HexText.Format(sample));
            bool hexOk = roundTrip.Length == sample.Length;
            if (hexOk)
            {
                for (int i = 0; i < sample.Length; i++)
                {
                    if (roundTrip[i] != sample[i])
                    {
                        hexOk = false;
                        break;
                    }
                }
            }
            check("十六进制文本往返一致", hexOk, string.Empty);

            List<string> fonts = FontCatalog.All;
            check("能够枚举系统字体", fonts.Count > 10, fonts.Count + " 款");
            bool hasYahei = false;
            foreach (string name in fonts)
            {
                if (string.Equals(name, "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase))
                {
                    hasYahei = true;
                    break;
                }
            }
            check("字体列表包含 Microsoft YaHei UI", hasYahei, string.Empty);

            try
            {
                MetricSnapshot snapshot = MetricReader.Capture();
                bool allPresent = true;
                foreach (string name in MetricCatalog.All)
                {
                    MetricValue value = snapshot[name];
                    if (value == null || !value.Exists || value.Data == null || value.Data.Length != LogFontCodec.Size)
                    {
                        allPresent = false;
                    }
                }
                check("读取六项窗口字体", allPresent, string.Empty);

                MetricValue menu = snapshot["MenuFont"];
                if (menu != null && menu.Data != null)
                {
                    report.AppendLine("      当前界面字体：" + LogFontCodec.GetFace(menu.Data));
                    report.AppendLine("      菜单字体高度：" + LogFontCodec.Height(menu.Data));
                }
            }
            catch (Exception ex)
            {
                check("读取六项窗口字体", false, ex.Message);
            }

            check("字号换算 -18 为 13.5pt",
                Math.Abs(FontCatalog.PointSizeForHeight(-18) - 13.5f) < 0.001f,
                FontCatalog.PointSizeForHeight(-18).ToString());

            report.AppendLine();
            report.AppendLine(failures == 0 ? "全部检查通过。" : ("有 " + failures + " 项检查失败。"));

            try
            {
                // 带 BOM，方便用 PowerShell / 记事本直接查看中文报告。
                File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "self-test.log"),
                    report.ToString(), new UTF8Encoding(true));
            }
            catch
            {
            }

            return failures == 0 ? 0 : 1;
        }
    }
}
