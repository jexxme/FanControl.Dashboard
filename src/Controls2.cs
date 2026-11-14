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
    /// <summary>Row of colour dots; the chosen one gets a ring. "Auto" (null) is the first entry when allowed.</summary>
    sealed class ColorPicker : StackPanel
    {
        readonly List<Tuple<string, Ellipse>> _rings = new List<Tuple<string, Ellipse>>();
        string _value;

        public ColorPicker(string label, string value, string autoColor, Action<string> changed, bool small = false)
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
                double d = small ? 10 : 14, r = small ? 15 : 20;
                var dot = new Ellipse { Width = d, Height = d, Fill = fill };
                var ring = new Ellipse { Width = r, Height = r, StrokeThickness = 1.5 };
                var grid = new Grid { Width = r, Height = r };
                grid.Children.Add(ring); grid.Children.Add(dot);
                if (name == null)
                {
                    // "automatic": the default colour with a small "A"
                    grid.Children.Add(new TextBlock { Text = "A", FontSize = small ? 7 : 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
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

        public void Tip(string text) { ToolTip = text; AutomationProperties.SetName(this, text); }
    }
}
