using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FanControl.Dashboard
{
    /// <summary>
    /// A Border that behaves like a flat button: hand cursor, hover and pressed backgrounds, keyboard focus
    /// (Tab, then Enter or Space), a focus ring, and a button automation peer for screen readers.
    /// </summary>
    class Pressable : Border
    {
        Action _click;
        Func<Brush> _rest = () => Brushes.Transparent;
        bool _pressed;

        public Pressable() { }
        public Pressable(string name, Action click, double radius) { Init(name, click, null, radius); }

        protected void Init(string name, Action click, Func<Brush> restBackground, double radius)
        {
            _click = click;
            if (restBackground != null) _rest = restBackground;
            CornerRadius = new CornerRadius(radius);
            Background = _rest();
            Cursor = Cursors.Hand;
            Focusable = true;
            FocusVisualStyle = Theme.FocusRing(radius);
            AutomationProperties.SetName(this, name);
            IsEnabledChanged += (s, e) => { Opacity = IsEnabled ? 1 : 0.45; Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow; Repaint(); };
        }

        public void SetRest(Func<Brush> rest) { _rest = rest; Repaint(); }

        public void Repaint()
        {
            Background = !IsEnabled ? _rest() : _pressed && IsMouseOver ? Palette.Track : IsMouseOver ? Palette.Hover : _rest();
        }

        public void Invoke() { if (IsEnabled && _click != null) _click(); }

        protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); Repaint(); }
        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); _pressed = false; Repaint(); }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); _pressed = true; Repaint(); e.Handled = true; }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            bool was = _pressed; _pressed = false; Repaint();
            if (was && IsMouseOver) Invoke();
            e.Handled = true;
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!e.Handled && (e.Key == Key.Enter || e.Key == Key.Space)) { Invoke(); e.Handled = true; }
        }

        protected override AutomationPeer OnCreateAutomationPeer() { return new Peer(this); }

        sealed class Peer : FrameworkElementAutomationPeer, IInvokeProvider
        {
            public Peer(Pressable owner) : base(owner) { }
            protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.Button; }
            protected override string GetClassNameCore() { return "Button"; }
            protected override bool IsControlElementCore() { return true; }
            protected override bool IsKeyboardFocusableCore() { return true; }
            public override object GetPattern(PatternInterface p) { return p == PatternInterface.Invoke ? this : base.GetPattern(p); }
            void IInvokeProvider.Invoke() { ((Pressable)Owner).Invoke(); }
        }
    }

    /// <summary>Round, flat icon button (Segoe Fluent / MDL2 glyph).</summary>
    sealed class IconButton : Pressable
    {
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        readonly TextBlock _glyph;
        bool _active;

        public IconButton(string glyph, string tip, Action click)
        {
            Width = 30; Height = 30; ToolTip = tip;
            _glyph = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, Foreground = Palette.TextDim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Child = _glyph;
            Init(tip, click, () => _active ? Palette.Track : Brushes.Transparent, 15);
        }

        public string Glyph { set { _glyph.Text = value; } }

        public string Tip { set { ToolTip = value; AutomationProperties.SetName(this, value); } }

        public bool Active
        {
            set { _active = value; _glyph.Foreground = value ? Palette.Text : Palette.TextDim; Repaint(); }
        }
    }

    /// <summary>Flat outlined text button.</summary>
    sealed class TextButton : Pressable
    {
        public TextButton(string text, Action click)
        {
            Padding = new Thickness(12, 5, 12, 5);
            BorderBrush = Palette.CardEdge; BorderThickness = new Thickness(1); VerticalAlignment = VerticalAlignment.Center;
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = Palette.Text };
            Init(text, click, null, 6);
        }
    }

    /// <summary>Small segmented toggle (two or more options). Arrow keys move the selection.</summary>
    sealed class Segmented : Border
    {
        readonly List<Pressable> _items = new List<Pressable>();
        readonly Action<int> _changed;
        int _selected = -1;

        public Segmented(string[] options, Action<int> changed, string name = null)
        {
            _changed = changed;
            BorderBrush = Palette.CardEdge; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Padding = new Thickness(2);
            if (name != null) AutomationProperties.SetName(this, name);
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var item = new Pressable(options[i], () => Choose(idx), 6) { Padding = new Thickness(10, 3, 10, 4), Child = new TextBlock { Text = options[i], FontSize = 12 } };
                item.SetRest(() => idx == _selected ? Palette.Track : Brushes.Transparent);
                if (i > 0) item.Margin = new Thickness(2, 0, 0, 0);
                item.PreviewKeyDown += (s, e) =>
                {
                    int to = e.Key == Key.Right ? idx + 1 : e.Key == Key.Left ? idx - 1 : -1;
                    if (to < 0 || to >= _items.Count) return;
                    Choose(to); _items[to].Focus(); e.Handled = true;
                };
                _items.Add(item); p.Children.Add(item);
            }
            Child = p;
            Select(0);
            IsEnabledChanged += (s, e) => Opacity = IsEnabled ? 1 : 0.45;
        }

        void Choose(int idx)
        {
            if (idx == _selected) return;
            Select(idx);
            _changed(idx);
        }

        public void Select(int idx)
        {
            _selected = idx;
            for (int i = 0; i < _items.Count; i++)
            {
                ((TextBlock)_items[i].Child).Foreground = i == idx ? Palette.Text : Palette.TextDim;
                ((TextBlock)_items[i].Child).FontWeight = i == idx ? FontWeights.SemiBold : FontWeights.Normal;
                AutomationProperties.SetItemStatus(_items[i], i == idx ? "selected" : "");
                _items[i].Repaint();
            }
        }
    }

    /// <summary>Thin horizontal meter: rounded track plus a filled part (0..100).</summary>
    sealed class Bar : FrameworkElement
    {
        readonly Brush _fill;
        float _value = float.NaN;
        public Bar(Brush fill) { _fill = fill; Height = 6; SnapsToDevicePixels = true; }
        public float Value { get { return _value; } set { if (value.Equals(_value)) return; _value = value; InvalidateVisual(); } }
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRoundedRectangle(Palette.Track, null, new Rect(0, 0, w, h), h / 2, h / 2);
            if (float.IsNaN(_value) || _value <= 0) return;
            double fw = Math.Max(h, w * Math.Min(100, _value) / 100.0);
            dc.DrawRoundedRectangle(_fill, null, new Rect(0, 0, fw, h), h / 2, h / 2);
        }
    }

    /// <summary>Column count of <see cref="ColumnsPanel"/>; pure so it can be tested.</summary>
    static class ColumnMath
    {
        /// <summary>
        /// As many equal columns as fit at <paramref name="minColumnWidth"/>, at most one per item,
        /// but never an uneven split that an even one with one column less would avoid (4 cards: 4, 2 or 1 columns, never 3 + 1).
        /// </summary>
        public static int Columns(int items, double width, double gap, double minColumnWidth, bool forceSingle)
        {
            if (items <= 0 || forceSingle || double.IsNaN(width)) return 1;
            int cols = (int)Math.Floor((width + gap) / (Math.Max(1, minColumnWidth) + gap));
            cols = Math.Max(1, Math.Min(items, cols));
            if (cols > 2 && items % cols != 0 && items % (cols - 1) == 0) cols--;
            return cols;
        }

        /// <summary>Width of one column.</summary>
        public static double ColumnWidth(double width, double gap, int cols) { return Math.Max(0, (width - gap * (cols - 1)) / cols); }
    }

    /// <summary>Equal-width columns: as many as fit at MinColumnWidth, rows as tall as their tallest item.</summary>
    sealed class ColumnsPanel : Panel
    {
        public double Gap { get; set; }
        public double MinColumnWidth { get; set; }
        public bool ForceSingleColumn { get; set; }

        int Columns(double w) { return ColumnMath.Columns(InternalChildren.Count, w, Gap, MinColumnWidth, ForceSingleColumn); }

        protected override Size MeasureOverride(Size available)
        {
            double w = double.IsInfinity(available.Width) ? 1000 : available.Width;
            int cols = Columns(w); double cw = ColumnMath.ColumnWidth(w, Gap, cols), total = 0, rowH = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var c = InternalChildren[i]; c.Measure(new Size(cw, double.PositiveInfinity));
                rowH = Math.Max(rowH, c.DesiredSize.Height);
                if (i % cols == cols - 1 || i == InternalChildren.Count - 1) { total += rowH + (total > 0 ? Gap : 0); rowH = 0; }
            }
            return new Size(w, total);
        }

        protected override Size ArrangeOverride(Size final)
        {
            int cols = Columns(final.Width); double cw = ColumnMath.ColumnWidth(final.Width, Gap, cols), y = 0;
            for (int start = 0; start < InternalChildren.Count; start += cols)
            {
                double rowH = 0;
                for (int i = start; i < Math.Min(start + cols, InternalChildren.Count); i++) rowH = Math.Max(rowH, InternalChildren[i].DesiredSize.Height);
                for (int i = start; i < Math.Min(start + cols, InternalChildren.Count); i++)
                    InternalChildren[i].Arrange(new Rect((i - start) * (cw + Gap), y, cw, rowH));
                y += rowH + Gap;
            }
            return final;
        }
    }
}
