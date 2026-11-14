# Security

## Reporting a vulnerability

请通过 GitHub 仓库的 **Security → Report a vulnerability** 私下报告，不要在公开 Issue 中粘贴完整的注册表快照、日志或系统隐私信息。

## Security model

- 只写当前用户注册表 `HKCU\Control Panel\Desktop\WindowMetrics` 下的六项窗口字体（`CaptionFont`、`SmCaptionFont`、`MenuFont`、`StatusFont`、`MessageFont`、`IconFont`），并且只改动每个 `LOGFONTW` 里的字体族名（偏移 28，64 字节）。
- 写入前后逐字节校验：除字体名外任何字段不一致就中止并回滚。
- 不修改任何系统级（HKLM）键值，不写 `FontSubstitutes`，不写 `FontLink`。
- 不修改 `C:\Windows\Fonts` 下的字体文件，不修改图标大小、图标间距、DPI 与文本缩放设置。
- 不需要管理员权限，不发起 UAC 提权。
- 不联网、不上传字体、不收集遥测；GUI 模式为单实例运行。
- 回退备份保存在 `HKCU\Software\WinFontReplacer\Backup` 与数据目录下的 `backup.txt`；数据目录默认是程序旁的 `WinFontReplacerData`，不可写时使用 `%LOCALAPPDATA%\WinFontReplacer`。

请在提交 Issue 前删除日志中的用户名、计算机名和个人路径。
