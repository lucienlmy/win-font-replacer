using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FontSwap
{
    /// <summary>Win32 LOGFONTW 结构（92 字节，字体名 32 个 UTF-16 字符）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NONCLIENTMETRICS
    {
        public uint cbSize;
        public int iBorderWidth;
        public int iScrollWidth;
        public int iScrollHeight;
        public int iCaptionWidth;
        public int iCaptionHeight;
        public LOGFONT lfCaptionFont;
        public int iSmCaptionWidth;
        public int iSmCaptionHeight;
        public LOGFONT lfSmCaptionFont;
        public int iMenuWidth;
        public int iMenuHeight;
        public LOGFONT lfMenuFont;
        public LOGFONT lfStatusFont;
        public LOGFONT lfMessageFont;
        public int iPaddedBorderWidth;
    }

    internal static class NativeMethods
    {
        public const uint SPI_GETNONCLIENTMETRICS = 0x0029;
        public const uint SPI_GETICONTITLELOGFONT = 0x001F;

        public const uint WM_SETTINGCHANGE = 0x001A;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref NONCLIENTMETRICS pvParam, uint fWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref LOGFONT pvParam, uint fWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        public const uint EM_SETCUEBANNER = 0x1501;
        public const uint WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessagePtr(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageText(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        public static IntPtr BroadcastSettingChange(string area)
        {
            IntPtr result;
            try
            {
                return SendMessageTimeout(
                    new IntPtr(0xffff), WM_SETTINGCHANGE, IntPtr.Zero, area,
                    SMTO_ABORTIFHUNG, 2000, out result);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }

    /// <summary>LOGFONTW 二进制编解码：只替换字体名，其余字段逐字节保持不变。</summary>
    internal static class LogFontCodec
    {
        public const int Size = 92;
        public const int FaceOffset = 28;
        public const int FaceBytes = 64;
        public const int MaxFaceLength = 31;

        public static string GetFace(byte[] data)
        {
            if (data == null || data.Length < FaceOffset + FaceBytes)
            {
                return string.Empty;
            }

            string name = Encoding.Unicode.GetString(data, FaceOffset, FaceBytes);
            int end = name.IndexOf('\0');
            if (end >= 0)
            {
                name = name.Substring(0, end);
            }
            return name.Trim();
        }

        /// <summary>复制原数据，只把 lfFaceName 改为 faceName，其他字节保持原样。</summary>
        public static byte[] WithFace(byte[] data, string faceName)
        {
            if (data == null || data.Length != Size)
            {
                throw new ArgumentException("LOGFONT 数据长度不是 92 字节。", "data");
            }
            if (faceName == null)
            {
                faceName = string.Empty;
            }

            byte[] result = (byte[])data.Clone();
            Array.Clear(result, FaceOffset, FaceBytes);

            string face = faceName.Trim();
            if (face.Length > 31)
            {
                face = face.Substring(0, 31);
            }
            byte[] bytes = Encoding.Unicode.GetBytes(face);
            if (bytes.Length > FaceBytes - 2)
            {
                Array.Resize(ref bytes, FaceBytes - 2);
            }
            Array.Copy(bytes, 0, result, FaceOffset, bytes.Length);
            return result;
        }

        public static bool SameExceptFace(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            int cutStart = FaceOffset;
            int cutEnd = Math.Min(FaceOffset + FaceBytes, a.Length);
            for (int i = 0; i < a.Length; i++)
            {
                if (i >= cutStart && i < cutEnd)
                {
                    continue;
                }
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        public static int Height(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                return 0;
            }
            return BitConverter.ToInt32(data, 0);
        }

        public static int Weight(byte[] data)
        {
            if (data == null || data.Length < 20)
            {
                return 400;
            }
            return BitConverter.ToInt32(data, 16);
        }

        public static byte[] FromStruct(LOGFONT lf)
        {
            IntPtr ptr = Marshal.AllocHGlobal(Size);
            try
            {
                Marshal.StructureToPtr(lf, ptr, false);
                byte[] data = new byte[Size];
                Marshal.Copy(ptr, data, 0, Size);
                return data;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>从系统当前默认字体构造 LOGFONT（用于注册表值缺失的兜底）。</summary>
        public static byte[] DefaultFor(string metricName)
        {
            if (string.Equals(metricName, "IconFont", StringComparison.OrdinalIgnoreCase))
            {
                LOGFONT icon = new LOGFONT();
                icon.lfHeight = -12;
                icon.lfWeight = 400;
                icon.lfCharSet = 1;
                icon.lfQuality = 5;
                icon.lfFaceName = "Segoe UI";
                if (NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETICONTITLELOGFONT, (uint)Size, ref icon, 0))
                {
                    return FromStruct(icon);
                }
                return FromStruct(icon);
            }

            NONCLIENTMETRICS metrics = new NONCLIENTMETRICS();
            metrics.cbSize = (uint)Marshal.SizeOf(typeof(NONCLIENTMETRICS));
            if (!NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0))
            {
                throw new InvalidOperationException("无法读取系统默认窗口字体（SPI_GETNONCLIENTMETRICS 失败）。");
            }

            if (string.Equals(metricName, "CaptionFont", StringComparison.OrdinalIgnoreCase))
            {
                return FromStruct(metrics.lfCaptionFont);
            }
            if (string.Equals(metricName, "SmCaptionFont", StringComparison.OrdinalIgnoreCase))
            {
                return FromStruct(metrics.lfSmCaptionFont);
            }
            if (string.Equals(metricName, "MenuFont", StringComparison.OrdinalIgnoreCase))
            {
                return FromStruct(metrics.lfMenuFont);
            }
            if (string.Equals(metricName, "StatusFont", StringComparison.OrdinalIgnoreCase))
            {
                return FromStruct(metrics.lfStatusFont);
            }
            return FromStruct(metrics.lfMessageFont);
        }
    }

    internal static class HexText
    {
        public static string Format(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(data[i].ToString("X2"));
            }
            return builder.ToString();
        }

        public static byte[] Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new byte[0];
            }

            StringBuilder digits = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (Uri.IsHexDigit(c))
                {
                    digits.Append(c);
                }
            }

            if (digits.Length == 0 || (digits.Length & 1) != 0)
            {
                return new byte[0];
            }

            byte[] data = new byte[digits.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Convert.ToByte(digits.ToString(i * 2, 2), 16);
            }
            return data;
        }
    }
}
