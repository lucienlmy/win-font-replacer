using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FontSwap
{
    internal static class Theme
    {
        public static readonly Color WindowBack = Color.FromArgb(244, 245, 247);
        public static readonly Color CardBack = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(228, 230, 235);
        public static readonly Color Divider = Color.FromArgb(241, 242, 244);
        public static readonly Color TextPrimary = Color.FromArgb(17, 24, 39);
        public static readonly Color TextSecondary = Color.FromArgb(107, 114, 128);
        public static readonly Color TextMuted = Color.FromArgb(156, 163, 175);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentDown = Color.FromArgb(30, 64, 175);
        public static readonly Color AccentSoft = Color.FromArgb(239, 246, 255);
        public static readonly Color AccentSoftBorder = Color.FromArgb(191, 219, 254);
        public static readonly Color Success = Color.FromArgb(21, 128, 61);
        public static readonly Color SuccessSoft = Color.FromArgb(236, 253, 245);
        public static readonly Color NeutralButton = Color.FromArgb(243, 244, 246);
        public static readonly Color NeutralButtonHover = Color.FromArgb(233, 235, 239);
        public static readonly Color NeutralButtonBorder = Color.FromArgb(228, 230, 235);
        public static readonly Color ToastBack = Color.FromArgb(31, 41, 55);
        public static readonly Color ToastErrorBack = Color.FromArgb(153, 27, 27);

        public const string UiFont = "Microsoft YaHei UI";

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || bounds.Width <= 1 || bounds.Height <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
        {
            using (GraphicsPath path = RoundedRect(bounds, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void StrokeRounded(Graphics g, Rectangle bounds, int radius, Color color, float width)
        {
            using (GraphicsPath path = RoundedRect(bounds, radius))
            using (Pen pen = new Pen(color, width))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, path);
            }
        }

        public static void DrawCard(Graphics g, Rectangle bounds, int radius, Color back, Color border)
        {
            FillRounded(g, bounds, radius, back);
            StrokeRounded(g, bounds, radius, border, 1f);
        }

        public static void Text(
            Graphics g,
            string text,
            Font font,
            Color color,
            Rectangle bounds,
            TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, flags);
        }

        public static void Text(
            Graphics g,
            string text,
            Font font,
            Color color,
            Point location)
        {
            TextRenderer.DrawText(g, text, font, location, color);
        }

        public static Color Mix(Color from, Color to, float amount)
        {
            if (amount <= 0f)
            {
                return from;
            }
            if (amount >= 1f)
            {
                return to;
            }
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }
    }
}
