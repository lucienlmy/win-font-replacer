using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FontSwap
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string command = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

            if (command == "--self-test")
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            if (command == "--apply" || command == "--restore" || command == "--status" || command == "--import-legacy")
            {
                Environment.ExitCode = RunCommand(command, args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            bool created;
            using (Mutex instance = new Mutex(true, "Local\\WinFontReplacer.UI", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Windows 全局字体替换器 已经在运行。", "Windows 全局字体替换器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    OperationLog.Write("界面未处理异常：" + e.Exception);
                    MessageBox.Show("发生未处理的错误：\r\n\r\n" + e.Exception.Message, "Windows 全局字体替换器",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    OperationLog.Write("未处理异常：" + e.ExceptionObject);
                };

                Application.Run(new MainForm());
            }
        }

        /// <summary>供脚本与排查使用：--apply "字体名" [--icon]、--restore、--status。</summary>
        private static int RunCommand(string command, string[] args)
        {
            StringBuilder output = new StringBuilder();
            int exitCode = 0;

            try
            {
                if (command == "--apply")
                {
                    if (args.Length < 2 || args[1] == null || args[1].Trim().Length == 0)
                    {
                        throw new ArgumentException("用法：WinFontReplacer.exe --apply \"字体名称\" [--icon]");
                    }
                    bool includeIcon = args.Length > 2 &&
                        string.Equals(args[2], "--icon", StringComparison.OrdinalIgnoreCase);
                    ApplyResult result = FontOperations.Apply(args[1], includeIcon);
                    output.AppendLine("OK 已应用字体：" + result.FontFamily);
                    output.AppendLine("替换项目：" + string.Join(", ", result.Changed.ToArray()));
                    if (result.Note.Length > 0)
                    {
                        output.AppendLine(result.Note);
                    }
                    AppendMetrics(output);
                }
                else if (command == "--restore")
                {
                    FontOperations.Restore();
                    output.AppendLine("OK 已回退到原始字体。");
                    AppendMetrics(output);
                }
                else if (command == "--import-legacy")
                {
                    string path = args.Length > 1 ? args[1] : string.Empty;
                    string message;
                    bool imported = string.IsNullOrEmpty(path)
                        ? LegacyImporter.TryImport(out message)
                        : LegacyImporter.ImportFrom(path, out message);
                    output.AppendLine((imported ? "OK " : "SKIP ") + message);
                    output.AppendLine("可回退：" + (FontOperations.HasBackup() ? "是" : "否"));
                }
                else
                {
                    bool hasBackup = FontOperations.HasBackup();
                    output.AppendLine("可回退：" + (hasBackup ? "是" : "否"));
                    if (hasBackup)
                    {
                        output.AppendLine("原始字体：" + FontOperations.OriginalFont());
                    }
                    List<string> legacy = LegacyImporter.FindCandidates();
                    if (legacy.Count > 0)
                    {
                        output.AppendLine("旧版备份：" + legacy[0]);
                    }
                    AppendMetrics(output);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine("失败：" + ex.Message);
                exitCode = 1;
            }

            try
            {
                // 带 BOM：命令行用户常用 Get-Content / 记事本查看，无 BOM 时中文会被当成 ANSI 解出乱码。
                File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "cli-result.txt"),
                    output.ToString(), new UTF8Encoding(true));
            }
            catch
            {
            }

            return exitCode;
        }

        private static void AppendMetrics(StringBuilder output)
        {
            MetricSnapshot snapshot = MetricReader.Capture();
            foreach (string name in MetricCatalog.All)
            {
                MetricValue value = snapshot[name];
                if (value == null || !value.Exists || value.Data == null)
                {
                    output.AppendLine("  " + name + " = <缺失>");
                    continue;
                }
                output.AppendLine("  " + name + " = " + LogFontCodec.GetFace(value.Data) +
                    " / 高度 " + LogFontCodec.Height(value.Data) +
                    " / " + value.Kind);
            }
        }
    }
}
