using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FanControl.Dashboard
{
    /// <summary>Text tabs with an accent underline on the selected one; wraps on narrow widths. Arrow keys move between tabs.</summary>
    sealed class TabStrip : WrapPanel
    {
        readonly List<Pressable> _tabs = new List<Pressable>();
        readonly List<Rectangle> _lines = new List<Rectangle>();
        readonly Action<int> _changed;
        int _selected = -1;

        public TabStrip(string[] names, Action<int> changed)
        {
            _changed = changed;
            AutomationProperties.SetName(this, "Settings sections");
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var text = new TextBlock { Text = names[i], FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center };
                var line = new Rectangle { Height = 2, RadiusX = 1, RadiusY = 1, Margin = new Thickness(0, 5, 0, 0), Fill = Brushes.Transparent };
                var stack = new StackPanel();
                stack.Children.Add(text); stack.Children.Add(line);
                var tab = new Pressable(names[i], () => Select(idx, true), 5) { Child = stack, Padding = new Thickness(10, 6, 10, 0), Margin = new Thickness(0, 0, 2, 0) };
                tab.PreviewKeyDown += (s, e) =>
                {
                    int to = e.Key == Key.Right ? idx + 1 : e.Key == Key.Left ? idx - 1 : -1;
                    if (to < 0 || to >= _tabs.Count) return;
                    Select(to, true); _tabs[to].Focus(); e.Handled = true;
                };
                _tabs.Add(tab); _lines.Add(line); Children.Add(tab);
            }
        }

        public int Selected { get { return _selected; } }

        public void Select(int idx, bool notify)
        {
            if (idx == _selected) return;
            _selected = idx;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var text = (TextBlock)((StackPanel)_tabs[i].Child).Children[0];
                text.Foreground = i == idx ? Palette.Text : Palette.TextDim;
                text.FontWeight = i == idx ? FontWeights.SemiBold : FontWeights.Normal;
                _lines[i].Fill = i == idx ? Palette.Accent : Brushes.Transparent;
                AutomationProperties.SetItemStatus(_tabs[i], i == idx ? "selected" : "");
            }
            if (notify) _changed(idx);
        }
    }

    /// <summary>Row of colour dots; the chosen one gets a ring. "Auto" (null) is the first entry when allowed.</summary>
    sealed class ColorPicker : StackPanel
    {
        readonly List<Tuple<string, Ellipse>> _rings = new List<Tuple<string, Ellipse>>();
        string _value;

        public ColorPicker(string label, string value, string autoColor, Action<string> changed)
        {
            Orientation = Orientation.Horizontal;
            _value = value;
            AutomationProperties.SetName(this, label + " colour");
            var names = new List<string>();
            if (autoColor != null) names.Add(null);
            names.AddRange(Palette.Choices);
            foreach (var n in names)
            {
                string name = n;
                var fill = Palette.Named(name ?? autoColor);
                var dot = new Ellipse { Width = 14, Height = 14, Fill = fill };
                var ring = new Ellipse { Width = 20, Height = 20, StrokeThickness = 1.5 };
                var grid = new Grid { Width = 20, Height = 20 };
                grid.Children.Add(ring); grid.Children.Add(dot);
                if (name == null)
                {
                    // "automatic": the default colour with a small "A"
                    grid.Children.Add(new TextBlock { Text = "A", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                }
                string title = name == null ? "Automatic (" + autoColor + ")" : name;
                var b = new Pressable(label + ": " + title, () => { _value = name; Paint(); changed(name); }, 11) { Child = grid, Padding = new Thickness(1), Margin = new Thickness(0, 0, 2, 0), ToolTip = title };
                _rings.Add(Tuple.Create(name, ring));
                Children.Add(b);
            }
            Paint();
        }

        void Paint()
        {
            foreach (var r in _rings) r.Item2.Stroke = r.Item1 == _value ? Palette.Text : Brushes.Transparent;
        }
    }

    /// <summary>Small square glyph button for list rows (move up/down, delete).</summary>
    sealed class RowButton : Pressable
    {
        public RowButton(string glyph, string name, Action click)
        {
            Init(name, click, null, 4);
            Width = 26; Height = 26; ToolTip = name;
            Child = new TextBlock { Text = glyph, FontFamily = IconButton.Icons, FontSize = 11, Foreground = Palette.TextDim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
    }
}
