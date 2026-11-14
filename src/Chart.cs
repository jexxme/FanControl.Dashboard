using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace FanControl.Dashboard
{
    /// <summary>One line in a chart.</summary>
    sealed class SeriesDef
    {
        public string Ref, Key, Name, Short, Unit; public Brush Brush;
        /// <summary>Limit line in the value's unit (°C for temperatures), NaN for none, and its label (e.g. "CPU limit").</summary>
        public Func<double> Limit = () => double.NaN;
        public string LimitLabel;
    }

    /// <summary>Line chart of the sampler history with grid, limit lines, direct labels and a (synced) hover crosshair.</summary>
    sealed class Chart : FrameworkElement
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static Pen GridPen { get { return Frozen(new Pen(Palette.Grid, 1)); } }
        static Pen CrossPen { get { return Frozen(new Pen(Palette.TextMuted, 1)); } }
        static Pen DashPen(Brush b) { return Frozen(new Pen(b, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }); }

        const double Top = 10, Bottom = 22, Gutter = 108;
        readonly Settings _settings;
        Point? _mouse;
        double? _sync;
        SeriesDef[] _series = new SeriesDef[0];

        public int Points { get; set; }
        /// <summary>Unit of every line in this chart ("°C", "%", "rpm", ...).</summary>
        public string Unit { get; set; }
        public Action<Chart, double> HoverChanged;
        public SeriesDef[] AllSeries { get { return _series; } }
        /// <summary>Whether a line has a sensor at all (set by the card); unavailable lines are neither drawn nor listed.</summary>
        public Func<string, bool> IsAvailable = r => true;
        /// <summary>Whether the user hid a line by clicking its legend entry.</summary>
        public Func<string, bool> IsHidden = r => false;
        SeriesDef[] Visible { get { return _series.Where(s => IsAvailable(s.Ref) && !IsHidden(s.Ref)).ToArray(); } }

        public Chart(Settings settings) { _settings = settings; Points = 600; ClipToBounds = true; Unit = "°C"; }

        internal static Sampler Source; // set by the view; one sampler per process

        public void SetSeries(SeriesDef[] series) { _series = series ?? new SeriesDef[0]; InvalidateVisual(); }

        static Pen Frozen(Pen p) { p.Freeze(); return p; }

        double LeftMargin { get { return Unit == "rpm" || Unit == "MHz" ? 44 : 36; } }
        Rect Plot { get { return new Rect(LeftMargin, Top, Math.Max(10, ActualWidth - LeftMargin - Gutter), Math.Max(10, ActualHeight - Top - Bottom)); } }

        public void Hover(double fraction)
        {
            var p = Plot;
            _mouse = new Point(p.Left + fraction * p.Width, p.Top + p.Height / 2);
            InvalidateVisual();
        }

        /// <summary>Crosshair driven by another chart (no tooltip).</summary>
        public void Sync(double fraction)
        {
            _sync = fraction < 0 ? (double?)null : fraction;
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _mouse = e.GetPosition(this); InvalidateVisual();
            var p = Plot;
            if (HoverChanged != null) HoverChanged(this, p.Contains(_mouse.Value) ? (_mouse.Value.X - p.Left) / p.Width : -1);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            _mouse = null; InvalidateVisual();
            if (HoverChanged != null) HoverChanged(this, -1);
        }

        FormattedText Text(string s, Brush b, double size = 11)
        {
            var tf = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return new FormattedText(s, Inv, FlowDirection.LeftToRight, tf, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        void Centered(DrawingContext dc, Rect plot, string message)
        {
            var t = Text(message, Palette.TextMuted, 12);
            t.MaxTextWidth = Math.Max(40, plot.Width - 20);
            var back = new Rect(plot.Left + (plot.Width - t.Width) / 2 - 10, plot.Top + (plot.Height - t.Height) / 2 - 5, t.Width + 20, t.Height + 10);
            dc.DrawRoundedRectangle(Palette.Card, null, back, 4, 4);
            dc.DrawText(t, new Point(back.Left + 10, back.Top + 5));
        }

        bool Temps { get { return Unit == "°C"; } }
        string UnitLabel { get { return Units.DisplayUnit(Unit); } }

        protected override void OnRender(DrawingContext dc)
        {
            var sampler = Source;
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H)); // hit-test surface
            if (sampler == null || W < 60 || H < 40) return;
            var plot = Plot;
            bool temps = Temps;
            Func<float, float> show = v => temps ? Units.ToDisplay(v) : v;

            var series = Visible;
            int window = Math.Max(1, _settings.Smoothing);
            var data = series.Select(s => ChartMath.Smooth(sampler.History(s.Key, Points), window).Select(show).ToArray()).ToArray();
            var limits = new List<Tuple<SeriesDef, double>>();
            if (_settings.ShowLimits)
                for (int s = 0; s < series.Length; s++)
                {
                    double l = series[s].Limit();
                    if (!double.IsNaN(l) && data[s].Any(v => !float.IsNaN(v))) limits.Add(Tuple.Create(series[s], temps ? Units.ToDisplay(l) : l));
                }

            var all = data.SelectMany(a => a).Where(v => !float.IsNaN(v)).ToList();
            double max = all.Count == 0 ? double.NaN : all.Max(), min = all.Count == 0 ? double.NaN : all.Min();
            foreach (var l in limits) { max = double.IsNaN(max) ? l.Item2 : Math.Max(max, l.Item2); min = double.IsNaN(min) ? l.Item2 : Math.Min(min, l.Item2); }
            double lo, hi, step;
            ChartMath.Axis(Unit, Units.Fahrenheit, min, max, plot.Height, out lo, out hi, out step);
            Func<double, double> Y = v => plot.Bottom - (Math.Max(lo, Math.Min(hi, v)) - lo) / (hi - lo) * plot.Height;
            Func<int, double> X = i => plot.Left + (Points <= 1 ? 0 : i * plot.Width / (Points - 1));

            // grid + y labels
            for (double v = lo; v <= hi + step * 0.01; v += step)
            {
                double y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var t = Text(ChartMath.Tick(v, step), Palette.TextMuted);
                dc.DrawText(t, new Point(plot.Left - 8 - t.Width, y - t.Height / 2));
            }
            // x labels
            int minutes = Points / 60;
            Func<double, string> ago = m => m >= 60 && m % 60 == 0 ? "-" + (m / 60).ToString("0", Inv) + " h" : "-" + m.ToString("0.#", Inv) + " min";
            var xl = new[] { Tuple.Create(0.0, ago(minutes)), Tuple.Create(0.5, ago(minutes / 2.0)), Tuple.Create(1.0, "now") };
            foreach (var l in xl)
            {
                var t = Text(l.Item2, Palette.TextMuted);
                double x = plot.Left + l.Item1 * plot.Width - (l.Item1 >= 1 ? t.Width : l.Item1 <= 0 ? 0 : t.Width / 2);
                dc.DrawText(t, new Point(x, plot.Bottom + 5));
            }
            // limit lines (their labels are drawn after the data so lines never cover them)
            var limitLabels = new List<Tuple<double, string>>();
            foreach (var l in limits)
            {
                double y = Math.Round(Y(l.Item2)) + 0.5;
                dc.DrawLine(DashPen(Palette.Limit), new Point(plot.Left, y), new Point(plot.Right, y));
                limitLabels.Add(Tuple.Create(y, l.Item1.LimitLabel + " " + ChartMath.Tick(l.Item2, 1) + " " + UnitLabel));
            }

            if (_series.Length == 0) { Centered(dc, plot, "No lines yet. Customize the dashboard to add some."); return; }
            if (series.Length == 0)
            {
                Centered(dc, plot, _series.Any(s => IsAvailable(s.Ref)) ? "All lines are hidden. Click a legend entry to show it again." : "Waiting for these sensors…");
                return;
            }
            if (data.All(a => a.All(float.IsNaN)))
            {
                Centered(dc, plot, sampler.Discovered ? "Collecting data…" : "Waiting for Fan Control…");
                return;
            }

            // lines: about two points per pixel, keeping each pixel's minimum and maximum
            int buckets = Math.Max(10, (int)plot.Width);
            for (int s = 0; s < series.Length; s++)
            {
                var pen = new Pen(series[s].Brush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    bool open = false;
                    foreach (int i in ChartMath.Decimate(data[s], buckets))
                    {
                        if (i < 0) { open = false; continue; }
                        var p = new Point(X(i), Y(data[s][i]));
                        if (!open) { ctx.BeginFigure(p, false, false); open = true; } else ctx.LineTo(p, true, true);
                    }
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }

            double lastLabel = double.NegativeInfinity;
            foreach (var l in limitLabels.OrderBy(l => l.Item1))
            {
                var t = Text(l.Item2, Palette.TextDim, 10.5);
                double ty = Math.Max(plot.Top, Math.Max(l.Item1 - t.Height - 2, lastLabel + 2));
                var pad = new Rect(plot.Left + 4, ty, t.Width + 8, t.Height);
                dc.DrawRoundedRectangle(Palette.Card, null, pad, 3, 3);
                dc.DrawText(t, new Point(pad.Left + 4, ty));
                lastLabel = pad.Bottom;
            }

            // direct labels at the line ends (collision-avoided, kept inside the plot height)
            var ends = new List<Tuple<double, SeriesDef, float>>();
            for (int s = 0; s < series.Length; s++)
            {
                int i = Array.FindLastIndex(data[s], v => !float.IsNaN(v));
                if (i < 0) continue;
                ends.Add(Tuple.Create(Y(data[s][i]), series[s], data[s][i]));
                dc.DrawEllipse(series[s].Brush, new Pen(Palette.Card, 2), new Point(X(i), Y(data[s][i])), 4, 4);
            }
            ends.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            var ys = new double[ends.Count];
            double last = double.NegativeInfinity;
            for (int k = 0; k < ends.Count; k++) { ys[k] = Math.Max(ends[k].Item1, last + 14); last = ys[k]; }
            double overflow = ends.Count == 0 ? 0 : ys[ends.Count - 1] - (plot.Bottom + 4);
            for (int k = ends.Count - 1; k >= 0 && overflow > 0; k--)
            {
                double limit = k == ends.Count - 1 ? plot.Bottom + 4 : ys[k + 1] - 14;
                if (ys[k] > limit) ys[k] = limit;
            }
            string endUnit = temps ? "°" : Unit == "%" ? " %" : "";
            for (int k = 0; k < ends.Count; k++)
            {
                var e = ends[k];
                var t = Text(e.Item3.ToString(ChartMath.ValueFormat(Unit), Inv) + endUnit + "  " + e.Item2.Short, Palette.Text, 11);
                t.MaxTextWidth = Gutter - 20; t.MaxLineCount = 1; t.Trimming = TextTrimming.CharacterEllipsis;
                // a short leader from the line end to its label, bent when the label had to move to avoid another one
                var leader = Frozen(new Pen(e.Item2.Brush, 1.5));
                dc.DrawLine(leader, new Point(plot.Right + 5, e.Item1), new Point(plot.Right + 9, ys[k]));
                dc.DrawLine(leader, new Point(plot.Right + 9, ys[k]), new Point(plot.Right + 13, ys[k]));
                dc.DrawText(t, new Point(plot.Right + 17, ys[k] - t.Height / 2));
            }

            // hover crosshair (+ tooltip when the mouse is on this chart)
            double? frac = null; bool own = false;
            if (_mouse.HasValue && plot.Contains(_mouse.Value)) { frac = (_mouse.Value.X - plot.Left) / plot.Width; own = true; }
            else if (_sync.HasValue) frac = _sync.Value;
            if (!frac.HasValue) return;
            {
                int i = (int)Math.Round(frac.Value * (Points - 1));
                i = Math.Max(0, Math.Min(Points - 1, i));
                double x = Math.Round(X(i)) + 0.5;
                dc.DrawLine(CrossPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var rows = new List<Tuple<SeriesDef, float>>();
                for (int s = 0; s < series.Length; s++)
                {
                    float v = data[s][i]; rows.Add(Tuple.Create(series[s], v));
                    if (!float.IsNaN(v)) dc.DrawEllipse(series[s].Brush, new Pen(Palette.Card, 2), new Point(X(i), Y(v)), 4.5, 4.5);
                }
                if (!own) return;
                int secs = Points - 1 - i;
                string when = secs == 0 ? "now" : secs < 60 ? secs + " s ago" : (secs / 60) + " min " + (secs % 60).ToString("00") + " s ago";
                var head = Text(when + (_settings.Smoothing > 0 ? " · smoothed" : ""), Palette.TextDim, 11);
                string fmt = Unit == "rpm" || Unit == "MHz" ? "0" : "0.0";
                var lines = rows.Select(r => Text(r.Item1.Name + "   " + (float.IsNaN(r.Item2) ? "–" : r.Item2.ToString(fmt, Inv) + " " + UnitLabel), Palette.Text, 11.5)).ToList();
                double bw = Math.Max(head.Width, lines.Max(l => l.Width) + 14) + 20, bh = 12 + head.Height + lines.Sum(l => l.Height + 3) + 6;
                double bx = x + 12; if (bx + bw > plot.Right) bx = Math.Max(2, x - 12 - bw); // keep clear of the end labels
                double by = Math.Max(plot.Top, Math.Min(_mouse.Value.Y - bh / 2, plot.Bottom - bh));
                dc.DrawRoundedRectangle(Palette.Tooltip, new Pen(Palette.CardEdge, 1), new Rect(bx, by, bw, bh), 6, 6);
                dc.DrawText(head, new Point(bx + 10, by + 7));
                double ly = by + 9 + head.Height + 3;
                for (int r = 0; r < lines.Count; r++)
                {
                    dc.DrawEllipse(rows[r].Item1.Brush, null, new Point(bx + 14, ly + lines[r].Height / 2), 3.5, 3.5);
                    dc.DrawText(lines[r], new Point(bx + 24, ly));
                    ly += lines[r].Height + 3;
                }
            }
        }
    }
}
