using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FanControl.Dashboard
{
    /// <summary>
    /// Colours. Neutrals are derived from the host's card colours at runtime (see <see cref="Theme.Adopt"/>);
    /// series and status colours have a dark and a light variant.
    /// </summary>
    static class Palette
    {
        public static Brush Card = Freeze("#1E293B");
        public static Brush CardEdge = Freeze("#334155");
        public static Brush Text = Freeze("#E2E8F0");
        public static Brush TextDim = Freeze("#94A3B8");
        public static Brush TextMuted = Freeze("#64748B");
        public static Brush Grid = Freeze("#2C3A50");
        public static Brush Track = Freeze("#334155");
        public static Brush Hover = Freeze("#273449");
        public static Brush Accent = Freeze("#67E8F9");
        public static Brush Tooltip = Freeze("#0F172A");
        public static Brush Warn, Critical, Limit;
        // categorical series slots
        public static Brush Cpu, Gpu, Hotspot, Vram, Ram;
        public static bool IsLight { get; private set; }

        static Palette() { SetMode(false); }

        /// <summary>Colours the user can pick for lines and cards: name, dark-theme hex, light-theme hex.</summary>
        public static readonly string[][] ChoiceTable =
        {
            new[] { "blue", "#3987E5", "#1F6FD1" }, new[] { "orange", "#D95926", "#C24E1C" }, new[] { "green", "#199E70", "#13855D" },
            new[] { "amber", "#C98500", "#9A6600" }, new[] { "violet", "#9085E9", "#6A5BD6" }, new[] { "pink", "#D9529B", "#B83A7E" },
            new[] { "teal", "#1BA5B8", "#0E7F8F" }, new[] { "red", "#E05252", "#C23B3B" }, new[] { "lime", "#7FB536", "#5A8A1C" },
            new[] { "slate", "#8A97A8", "#5E6B7C" },
        };
        public static readonly string[] Choices = ChoiceTable.Select(c => c[0]).ToArray();
        static readonly string[] CustomCycle = { "pink", "teal", "lime", "red", "slate", "amber", "green", "violet", "blue", "orange" };

        public static bool IsChoice(string name) { return name != null && Array.IndexOf(Choices, name) >= 0; }

        public static Brush Named(string name)
        {
            var row = ChoiceTable.FirstOrDefault(c => c[0] == name) ?? ChoiceTable[0];
            return Freeze(IsLight ? row[2] : row[1]);
        }

        /// <summary>Colour name a series gets unless the user picked one.</summary>
        public static string DefaultColor(string key, int customIndex)
        {
            switch (key)
            {
                case "cpu.temp": case "cpu.load": return "blue";
                case "gpu.temp": case "gpu.load": return "orange";
                case "gpu.hot": return "green";
                case "gpu.vram": return "amber";
                case "ram.temp": case "ram.load": return "violet";
                default: return CustomCycle[Math.Max(0, customIndex) % CustomCycle.Length];
            }
        }

        /// <summary>The colour of a series (built-in key or custom:&lt;id&gt;) under the current settings.</summary>
        public static Brush ForSeries(Settings s, string key)
        {
            string name;
            if (s != null && s.Colors.TryGetValue(key, out name) && IsChoice(name)) return Named(name);
            var custom = s == null ? null : s.FindCustom(key);
            if (custom != null && key.StartsWith("custom:", StringComparison.Ordinal))
                return Named(IsChoice(custom.Color) ? custom.Color : DefaultColor(key, s.Custom.IndexOf(custom)));
            return Named(DefaultColor(key, 0));
        }

        /// <summary>Series and status colours for a dark or a light card surface (checked for contrast on both).</summary>
        public static void SetMode(bool light)
        {
            IsLight = light;
            Cpu = Freeze(light ? "#1F6FD1" : "#3987E5");
            Gpu = Freeze(light ? "#C24E1C" : "#D95926");
            Hotspot = Freeze(light ? "#13855D" : "#199E70");
            Vram = Freeze(light ? "#9A6600" : "#C98500");
            Ram = Freeze(light ? "#6A5BD6" : "#9085E9");
            Limit = Freeze(light ? "#D04545" : "#E66767");
            Warn = Freeze(light ? "#9A5B00" : "#F0B35A");
            Critical = Freeze(light ? "#C62828" : "#FF8A80");
        }

        /// <summary>Derives every neutral from the host card's background and text colours.</summary>
        public static void FromCard(Color bg, Color fg)
        {
            // never trust a foreground that would be unreadable on the card (e.g. an inherited default of black on a dark card)
            if (Contrast(bg, fg) < 4.5) fg = Luminance(bg) > 0.5 ? Color.FromRgb(0x1F, 0x1F, 0x1F) : Color.FromRgb(0xF2, 0xF2, 0xF2);
            SetMode(Luminance(bg) > 0.5);
            Card = Freeze(bg);
            Text = Freeze(fg);
            TextDim = Freeze(Mix(bg, fg, 0.72));
            TextMuted = Freeze(Mix(bg, fg, 0.52));
            Grid = Freeze(Mix(bg, fg, IsLight ? 0.10 : 0.12));
            Hover = Freeze(Mix(bg, fg, 0.08));
            Track = Freeze(Mix(bg, fg, IsLight ? 0.13 : 0.16));
            CardEdge = Freeze(Mix(bg, fg, 0.22));
            Tooltip = Freeze(IsLight ? Mix(bg, Colors.White, 0.6) : Mix(bg, Colors.Black, 0.35));
        }

        /// <summary>Uses the host's accent colour, pulled towards the text colour until it stands out from the card (3:1).</summary>
        public static void SetAccent(Color accent)
        {
            Color bg = ((SolidColorBrush)Card).Color, fg = ((SolidColorBrush)Text).Color, c = accent;
            for (double t = 0.1; Contrast(c, bg) < 3 && t <= 1; t += 0.1) c = Mix(accent, Color.FromRgb(fg.R, fg.G, fg.B), t);
            Accent = Freeze(c);
        }

        public static Brush Freeze(string hex) { return Freeze((Color)ColorConverter.ConvertFromString(hex)); }
        public static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }
        public static double Luminance(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }

        /// <summary>WCAG contrast ratio of two opaque colours (1 to 21).</summary>
        public static double Contrast(Color a, Color b)
        {
            Func<byte, double> lin = v => { double c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); };
            Func<Color, double> rel = c => 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
            double la = rel(a), lb = rel(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }

    /// <summary>Look of the native cards and section headers, copied from the live Home page.</summary>
    public static class Theme
    {
        public static Type CardType;
        public static Style CardStyle;
        public static Brush CardBackground;
        public static Style HeaderStyle;
        public static double HeaderSize = 15;
        public static FontWeight HeaderWeight = FontWeights.SemiBold;
        public static bool Adopted;
        static WeakReference _source = new WeakReference(null);
        static Color _sourceColor;

        /// <summary>Reads the first native card and section header found under the home page.</summary>
        public static bool Adopt(DependencyObject home, DependencyObject exclude)
        {
            Control card = null, curveCard = null; TextBlock header = null;
            Walk(home, exclude, d =>
            {
                if (curveCard == null && d.GetType().Name == "GraphFanCurveCard")
                    Walk(d, null, x => { if (x.GetType().Name == "Card") { curveCard = (Control)x; return false; } return true; });
                return curveCard == null;
            });
            Walk(home, exclude, d =>
            {
                var c = d as Control;
                if (card == null && c != null && d.GetType().Name == "Card" && c.ActualWidth > 0) card = curveCard ?? c;
                var t = d as TextBlock;
                if (header == null && t != null && t.FontSize >= 14 && t.FontSize <= 22 && !string.IsNullOrEmpty(t.Text) && t.IsVisible) header = t;
                return card == null || header == null;
            });
            if (card == null) return false;
            CardType = card.GetType(); CardStyle = card.Style;
            var bg = card.Background as SolidColorBrush;
            CardBackground = card.Background;
            var fg = (card.GetValue(TextElement.ForegroundProperty) as SolidColorBrush) ?? card.Foreground as SolidColorBrush;
            if (bg != null && fg != null) Palette.FromCard(bg.Color, fg.Color);
            var accent = Application.Current == null ? null : Application.Current.TryFindResource("SecondaryHueMidBrush") as SolidColorBrush;
            Palette.SetAccent(accent != null ? accent.Color : ((SolidColorBrush)Palette.Accent).Color);
            if (header != null) { HeaderStyle = header.Style; HeaderSize = header.FontSize; HeaderWeight = header.FontWeight; }
            _source = new WeakReference(card);
            _sourceColor = bg != null ? bg.Color : Colors.Transparent;
            Adopted = true;
            return true;
        }

        /// <summary>True when the native card we copied from now has another background (the user switched Fan Control's theme).</summary>
        public static bool SourceChanged()
        {
            var card = _source.Target as Control;
            if (card == null) return false;
            var bg = card.Background as SolidColorBrush;
            return bg != null && bg.Color != _sourceColor;
        }

        /// <summary>Keyboard focus ring for the dashboard's own clickable elements (shown only for keyboard focus).</summary>
        public static Style FocusRing(double radius)
        {
            var rect = new FrameworkElementFactory(typeof(Rectangle));
            rect.SetValue(Shape.StrokeProperty, Palette.Accent);
            rect.SetValue(Shape.StrokeThicknessProperty, 1.5);
            rect.SetValue(Rectangle.RadiusXProperty, radius + 2);
            rect.SetValue(Rectangle.RadiusYProperty, radius + 2);
            rect.SetValue(FrameworkElement.MarginProperty, new Thickness(-2));
            rect.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            var style = new Style();
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate { VisualTree = rect }));
            return style;
        }

        static bool Walk(DependencyObject d, DependencyObject exclude, Func<DependencyObject, bool> visit)
        {
            if (d == null || ReferenceEquals(d, exclude)) return true;
            if (!visit(d)) return false;
            int n = d is Visual ? VisualTreeHelper.GetChildrenCount(d) : 0;
            for (int i = 0; i < n; i++) if (!Walk(VisualTreeHelper.GetChild(d, i), exclude, visit)) return false;
            return true;
        }
    }
}
