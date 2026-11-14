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
    /// <summary>Line chart of the sampler history with grid, limit lines, direct labels and a (synced) hover crosshair.</summary>
    sealed class Chart : FrameworkElement
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // built from the palette when the chart is created, so a theme switch (which recreates the view) recolours them
        readonly SeriesDef[] _temp = {
            new SeriesDef("CPU", "cpu.temp", Palette.Cpu, "°C"), new SeriesDef("GPU", "gpu.temp", Palette.Gpu, "°C"),
            new SeriesDef("GPU hotspot", "gpu.hot", Palette.Hotspot, "°C", "Hotspot"), new SeriesDef("VRAM", "gpu.vram", Palette.Vram, "°C"),
            new SeriesDef("RAM", "ram.temp", Palette.Ram, "°C") };
        readonly SeriesDef[] _load = {
            new SeriesDef("CPU", "cpu.load", Palette.Cpu, "%"), new SeriesDef("GPU", "gpu.load", Palette.Gpu, "%"),
            new SeriesDef("RAM", "ram.load", Palette.Ram, "%") };
        static Pen GridPen { get { return Frozen(new Pen(Palette.Grid, 1)); } }
        static Pen CrossPen { get { return Frozen(new Pen(Palette.TextMuted, 1)); } }
        static Pen DashPen(Brush b) { return Frozen(new Pen(b, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }); }

        const double Left = 36, Top = 10, Bottom = 22, Gutter = 96;
        readonly Settings _settings;
        readonly Sampler _sampler;
        Point? _mouse;
        double? _sync;

        public int Mode { get; set; }
        public int Points { get; set; }
        public Action<Chart, double> HoverChanged;
        public SeriesDef[] AllSeries { get { return Mode == 0 ? _temp : _load; } }
        /// <summary>Whether a series has a sensor at all (set by the card); unavailable series are neither drawn nor listed.</summary>
        public Func<string, bool> IsAvailable = key => true;
        SeriesDef[] Visible { get { return AllSeries.Where(s => IsAvailable(s.Key) && !_settings.IsHidden(Mode, s.Key)).ToArray(); } }

        public Chart(Settings settings) : this(settings, null) { }
        public Chart(Settings settings, Sampler sampler) { _settings = settings; _sampler = sampler; Points = 600; ClipToBounds = true; }

        internal static Sampler Source; // set by the view; one sampler per process

        static Pen Frozen(Pen p) { p.Freeze(); return p; }

        Rect Plot { get { return new Rect(Left, Top, Math.Max(10, ActualWidth - Left - Gutter), Math.Max(10, ActualHeight - Top - Bottom)); } }

        public void Hover(double fraction)
        {
            var p = Plot;
            _mouse = new Point(p.Left + fraction * p.Width, p.Top + p.Height / 2);
            InvalidateVisual();
        }

        /// <summary>Crosshair driven by the other chart (no tooltip).</summary>
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
            var back = new Rect(plot.Left + (plot.Width - t.Width) / 2 - 10, plot.Top + (plot.Height - t.Height) / 2 - 5, t.Width + 20, t.Height + 10);
            dc.DrawRoundedRectangle(Palette.Card, null, back, 4, 4);
            dc.DrawText(t, new Point(back.Left + 10, back.Top + 5));
        }

        protected override void OnRender(DrawingContext dc)
        {
            var sampler = _sampler ?? Source;
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H)); // hit-test surface
            if (sampler == null || W < 60 || H < 40) return;
            var plot = Plot;

            var series = Visible;
            var data = series.Select(s => sampler.History(s.Key, Points)).ToArray();
            bool ramShown = Mode == 0 && series.Any(s => s.Key == "ram.temp");
            bool cpuShown = Mode == 0 && series.Any(s => s.Key == "cpu.temp");
            double lo, hi, step;
            if (Mode == 0)
            {
                var all = data.SelectMany(a => a).Where(v => !float.IsNaN(v)).ToList();
                double max = all.DefaultIfEmpty(0).Max(), min = all.DefaultIfEmpty(40).Min();
                if (_settings.ShowLimits && cpuShown) max = Math.Max(max, _settings.CpuLimit);
                lo = min < 30 ? 20 : 30; hi = Math.Max(100, Math.Ceiling((max + 2) / 10) * 10); step = hi - lo > 80 ? 20 : 10;
                if (plot.Height < 150) step *= 2;
            }
            else { lo = 0; hi = 100; step = plot.Height < 150 ? 50 : 25; }
            Func<double, double> Y = v => plot.Bottom - (Math.Max(lo, Math.Min(hi, v)) - lo) / (hi - lo) * plot.Height;
            Func<int, double> X = i => plot.Left + (Points <= 1 ? 0 : i * plot.Width / (Points - 1));

            // grid + y labels
            for (double v = lo; v <= hi + 0.01; v += step)
            {
                double y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var t = Text(v.ToString("0", Inv), Palette.TextMuted);
                dc.DrawText(t, new Point(Left - 8 - t.Width, y - t.Height / 2));
            }
            // x labels
            int minutes = Points / 60;
            var xl = new[] { Tuple.Create(0.0, "-" + minutes + " min"), Tuple.Create(0.5, "-" + (minutes / 2.0).ToString("0.#", Inv) + " min"), Tuple.Create(1.0, "now") };
            foreach (var l in xl)
            {
                var t = Text(l.Item2, Palette.TextMuted);
                double x = plot.Left + l.Item1 * plot.Width - (l.Item1 >= 1 ? t.Width : l.Item1 <= 0 ? 0 : t.Width / 2);
                dc.DrawText(t, new Point(x, plot.Bottom + 5));
            }
            // limit lines (their labels are drawn after the data so lines never cover them)
            var limitLabels = new List<Tuple<double, string>>();
            if (_settings.ShowLimits && cpuShown)
            {
                double y = Math.Round(Y(_settings.CpuLimit)) + 0.5;
                dc.DrawLine(DashPen(Palette.Limit), new Point(plot.Left, y), new Point(plot.Right, y));
                limitLabels.Add(Tuple.Create(y, "CPU limit " + _settings.CpuLimit.ToString("0", Inv) + " °C"));
            }
            double ramWarning = _settings.EffectiveRamWarning(sampler.Get("ram.limit"));
            if (_settings.ShowLimits && ramShown && data[Array.FindIndex(series, x => x.Key == "ram.temp")].Any(v => !float.IsNaN(v)))
            {
                double y = Math.Round(Y(ramWarning)) + 0.5;
                dc.DrawLine(DashPen(Palette.Ram), new Point(plot.Left, y), new Point(plot.Right, y));
                limitLabels.Add(Tuple.Create(y, "RAM warning " + ramWarning.ToString("0", Inv) + " °C"));
            }

            if (series.Length == 0)
            {
                Centered(dc, plot, "All lines are hidden. Click a legend entry to show it again.");
                return;
            }
            if (data.All(a => a.All(float.IsNaN)))
            {
                Centered(dc, plot, sampler.Discovered ? "Collecting data…" : "Waiting for Fan Control…");
                return;
            }

            // lines
            for (int s = 0; s < series.Length; s++)
            {
                var pen = new Pen(series[s].Brush, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    bool open = false;
                    for (int i = 0; i < data[s].Length; i++)
                    {
                        float v = data[s][i];
                        if (float.IsNaN(v)) { open = false; continue; }
                        var p = new Point(X(i), Y(v));
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
            for (int k = 0; k < ends.Count; k++)
            {
                var e = ends[k];
                var t = Text(e.Item3.ToString("0", Inv) + (e.Item2.Unit == "%" ? " %" : "°") + "  " + e.Item2.Short, Palette.Text, 11);
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
                int ago = Points - 1 - i;
                var head = Text(ago == 0 ? "now" : ago < 60 ? ago + " s ago" : (ago / 60) + " min " + (ago % 60).ToString("00") + " s ago", Palette.TextDim, 11);
                var lines = rows.Select(r => Text(r.Item1.Name + "   " + (float.IsNaN(r.Item2) ? "–" : r.Item2.ToString("0.0", Inv) + " " + r.Item1.Unit), Palette.Text, 11.5)).ToList();
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
