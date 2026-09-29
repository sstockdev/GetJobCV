using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GetJobCV.UI
{
    /// <summary>
    /// Colors a skill chip is drawn with.
    /// </summary>
    internal sealed record ChipStyle(Color Ink, Color Fill, Color Border);

    /// <summary>
    /// Shared colors, fonts, and drawing helpers. Warm paper neutrals with one blue accent;
    /// orange is kept for missing skills so it differs from blue in lightness, not only hue.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Background = FromHex(0xF4F1EA);
        public static readonly Color Rail = FromHex(0xFAF8F3);
        public static readonly Color Surface = FromHex(0xFFFDF8);
        public static readonly Color Ink = FromHex(0x1C1B18);
        public static readonly Color Muted = FromHex(0x5E5A52);
        public static readonly Color Border = FromHex(0xDDD7CB);
        public static readonly Color ControlBorder = FromHex(0xCFC8BA);
        public static readonly Color Track = FromHex(0xECE7DD);
        public static readonly Color Accent = FromHex(0x1F4F8A);
        public static readonly Color AccentSoft = FromHex(0x6E8FB8);
        public static readonly Color AccentTint = FromHex(0xF3F6FB);

        public static readonly ChipStyle Matched = new(FromHex(0x1F4F8A), FromHex(0xE8EFF8), FromHex(0xC3D4EA));
        public static readonly ChipStyle Missing = new(FromHex(0x8A3B12), FromHex(0xFBEDE2), FromHex(0xEBC6A8));
        public static readonly ChipStyle Extra = new(FromHex(0x3F3B34), FromHex(0xF1EDE5), FromHex(0xDDD7CB));

        // First installed family wins, so installing the design's fonts picks them up
        private static readonly string DisplayFamily = Pick("Newsreader", "Georgia");
        private static readonly string BodyFamily = Pick("IBM Plex Sans", "Segoe UI");
        private static readonly string MonoFamily = Pick("IBM Plex Mono", "Cascadia Mono", "Consolas");

        public static Font Display(float size) => new(DisplayFamily, size, FontStyle.Bold);
        public static Font Body(float size, FontStyle style = FontStyle.Regular) => new(BodyFamily, size, style);
        public static Font Mono(float size) => new(MonoFamily, size);

        public static Font Caption { get; } = Body(8.25f, FontStyle.Bold);

        private static Color FromHex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        private static string Pick(params string[] names)
        {
            using InstalledFontCollection installed = new();
            HashSet<string> have = new(installed.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            return names.FirstOrDefault(have.Contains) ?? SystemFonts.MessageBoxFont!.FontFamily.Name;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath path = new();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// An uppercase section caption, like "RESUME" or "EDUCATION".
        /// </summary>
        public static Label CaptionLabel(string text) => new()
        {
            Text = text.ToUpperInvariant(),
            Font = Caption,
            ForeColor = Muted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };

        public static Label TextLabel(string text, Font font, Color color) => new()
        {
            Text = text,
            Font = font,
            ForeColor = color,
            AutoSize = true,
            Margin = new Padding(0),
        };

        public enum ButtonKind { Primary, Dark, Secondary, Link }

        public static Button Button(string text, ButtonKind kind)
        {
            Button b = new()
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Font = Body(10f, kind == ButtonKind.Link ? FontStyle.Regular : FontStyle.Bold),
                Height = 44,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 0, 12, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            (Color back, Color fore, Color border) = kind switch
            {
                ButtonKind.Primary => (Accent, Color.White, Accent),
                ButtonKind.Dark => (Ink, Surface, Ink),
                ButtonKind.Secondary => (Surface, Ink, ControlBorder),
                _ => (Surface, Accent, Surface),
            };
            b.FlatAppearance.BorderSize = kind == ButtonKind.Link ? 0 : 1;

            // Flat buttons keep their fill when disabled, so a disabled one looks clickable
            void Restyle()
            {
                b.BackColor = b.Enabled ? back : Track;
                b.FlatAppearance.BorderColor = b.Enabled ? border : ControlBorder;
                b.ForeColor = b.Enabled ? fore : Muted;
                b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
            }
            b.EnabledChanged += (_, _) => Restyle();
            Restyle();
            return b;
        }

        /// <summary>
        /// Lets PDFs be dropped anywhere on <paramref name="root"/> and its children.
        /// </summary>
        public static void AcceptPdfDrops(Control root, Action<string> dropped)
        {
            root.AllowDrop = true;
            root.DragEnter += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsPdf))
                    e.Effect = DragDropEffects.Copy;
            };
            root.DragDrop += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault(IsPdf) is { } pdf)
                    dropped(pdf);
            };
            root.ControlAdded += (_, e) => { if (e.Control is not null) AcceptPdfDrops(e.Control, dropped); };
            foreach (Control child in root.Controls)
                AcceptPdfDrops(child, dropped);
        }

        private static bool IsPdf(string path) => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Opens the resume picker. Null if the user cancelled.
        /// </summary>
        public static string? BrowseForResume(IWin32Window owner)
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                RestoreDirectory = true,
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }
    }
}
