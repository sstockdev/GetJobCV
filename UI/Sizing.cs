using System.ComponentModel;
namespace GetJobCV.UI
{
    /// <summary>
    /// Width-driven layout helpers. The built-in AutoSize panels size wrapping content to
    /// one long line, so the report lays itself out by asking each child how tall it is
    /// at a given width instead.
    /// </summary>
    internal static class Sizing
    {
        /// <summary>
        /// How tall <paramref name="control"/> is at <paramref name="width"/>.
        /// </summary>
        public static int HeightFor(Control control, int width) => control switch
        {
            // Fixed-size controls first: the ring is a Label only for screen readers
            ButtonBase or MeterBar or ScoreRing => control.Height,
            // Ellipsis labels stay on one line
            Label { AutoSize: false, AutoEllipsis: true } label => TextRenderer.MeasureText("Ag", label.Font).Height,
            // The label's own measurement wraps exactly the way it paints
            Label { AutoSize: false } label => label.GetPreferredSize(new Size(Math.Max(2, width), 0)).Height,
            FlowLayoutPanel flow => FlowHeight(flow, width),
            Panel { Controls.Count: 0 } => control.Height,
            _ => control.GetPreferredSize(new Size(width, 0)).Height,
        };

        /// <summary>
        /// Wrapped height of a left-to-right flow of fixed-size children.
        /// </summary>
        private static int FlowHeight(FlowLayoutPanel flow, int width)
        {
            int x = 0, rowHeight = 0, total = 0;
            foreach (Control child in flow.Controls)
            {
                int w = child.Width + child.Margin.Horizontal;
                int h = child.Height + child.Margin.Vertical;
                if (x > 0 && x + w > width)
                {
                    total += rowHeight;
                    x = 0;
                    rowHeight = 0;
                }
                x += w;
                rowHeight = Math.Max(rowHeight, h);
            }
            return total + rowHeight + flow.Padding.Vertical;
        }

        /// <summary>
        /// Children in layout order. A hidden view reports every child invisible, so
        /// children are removed rather than hidden.
        /// </summary>
        public static IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>();
    }

    /// <summary>
    /// Children top to bottom, each full width except buttons, which keep their size.
    /// </summary>
    internal class Stack : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Gap { get; set; }

        public Stack()
        {
            Margin = new Padding(0);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int inner = proposedSize.Width - Padding.Horizontal;
            int height = Padding.Vertical;
            int count = 0;
            foreach (Control child in Sizing.Children(this))
            {
                height += Sizing.HeightFor(child, inner - child.Margin.Horizontal) + child.Margin.Vertical;
                count++;
            }
            return new Size(proposedSize.Width, height + Math.Max(0, count - 1) * LogicalToDeviceUnits(Gap));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int inner = ClientSize.Width - Padding.Horizontal;
            int y = Padding.Top;
            foreach (Control child in Sizing.Children(this))
            {
                Padding m = child.Margin;
                int width = child is ButtonBase ? child.Width : inner - m.Horizontal;
                int height = Sizing.HeightFor(child, inner - m.Horizontal);
                child.SetBounds(Padding.Left + m.Left, y + m.Top, width, height);
                y += height + m.Vertical + LogicalToDeviceUnits(Gap);
            }
        }
    }

    /// <summary>
    /// Children left to right, vertically centered. The child tagged <see cref="Fill"/>
    /// takes the width the others leave.
    /// </summary>
    internal sealed class Row : Panel
    {
        public const string Fill = "fill";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Gap { get; set; }

        public Row()
        {
            Margin = new Padding(0);
        }

        private List<(Control Child, int Width)> Measure(int width)
        {
            List<Control> children = [.. Sizing.Children(this)];
            int gaps = Math.Max(0, children.Count - 1) * LogicalToDeviceUnits(Gap);
            int fixedWidth = children.Where(c => !Fill.Equals(c.Tag))
                .Sum(c => c.GetPreferredSize(Size.Empty).Width + c.Margin.Horizontal);
            int fill = Math.Max(0, width - fixedWidth - gaps);
            return [.. children.Select(c => (c, Fill.Equals(c.Tag)
                ? fill - c.Margin.Horizontal
                : c.GetPreferredSize(Size.Empty).Width))];
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int height = Measure(proposedSize.Width)
                .Select(m => Sizing.HeightFor(m.Child, m.Width) + m.Child.Margin.Vertical)
                .DefaultIfEmpty(0).Max();
            return new Size(proposedSize.Width, height);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int x = 0;
            foreach ((Control child, int width) in Measure(ClientSize.Width))
            {
                int height = Sizing.HeightFor(child, width);
                child.SetBounds(x + child.Margin.Left, (ClientSize.Height - height) / 2, Math.Max(0, width), height);
                x += width + child.Margin.Horizontal + LogicalToDeviceUnits(Gap);
            }
        }
    }

    /// <summary>
    /// Equal-width children side by side, as tall as the tallest unless
    /// <see cref="EqualHeights"/> is off. Stacks them instead when a column would be
    /// narrower than <see cref="MinColumnWidth"/>.
    /// </summary>
    internal sealed class Columns : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Gap { get; set; } = 20;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int MinColumnWidth { get; set; }

        /// <summary>
        /// Stretch every column to the tallest one. Off: each keeps its own height, top-aligned.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EqualHeights { get; set; } = true;

        public Columns()
        {
            Margin = new Padding(0);
        }

        private int ColumnWidth(int width)
        {
            int count = Controls.Count;
            return count == 0 ? 0 : (width - (count - 1) * LogicalToDeviceUnits(Gap)) / count;
        }

        private bool Stacked(int width) => ColumnWidth(width) < LogicalToDeviceUnits(MinColumnWidth);

        public override Size GetPreferredSize(Size proposedSize)
        {
            IEnumerable<Control> children = Controls.Cast<Control>();
            if (Stacked(proposedSize.Width))
                return new Size(proposedSize.Width, children.Sum(c => Sizing.HeightFor(c, proposedSize.Width)) +
                    Math.Max(0, Controls.Count - 1) * LogicalToDeviceUnits(Gap));

            int column = ColumnWidth(proposedSize.Width);
            return new Size(proposedSize.Width, children.Select(c => Sizing.HeightFor(c, column)).DefaultIfEmpty(0).Max());
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int width = ClientSize.Width;
            if (Stacked(width))
            {
                int y = 0;
                foreach (Control child in Controls)
                {
                    int height = Sizing.HeightFor(child, width);
                    child.SetBounds(0, y, width, height);
                    y += height + LogicalToDeviceUnits(Gap);
                }
                return;
            }

            int column = ColumnWidth(width);
            int x = 0;
            foreach (Control child in Controls)
            {
                child.SetBounds(x, 0, column, EqualHeights ? ClientSize.Height : Sizing.HeightFor(child, column));
                x += column + LogicalToDeviceUnits(Gap);
            }
        }
    }
}
