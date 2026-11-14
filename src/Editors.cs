using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FanControl.Dashboard
{
    /// <summary>Shared building blocks of the editors.</summary>
    static class Ui
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string GlyphClose = "", GlyphUp = "", GlyphDown = "", GlyphLeft = "", GlyphRight = "",
            GlyphDelete = "", GlyphEdit = "", GlyphAdd = "", GlyphEye = "", GlyphEyeOff = "", GlyphGrip = "",
            GlyphCheck = "", GlyphSearch = "", GlyphSettings = "";

        public static TextBlock Hint(string text)
        {
            var t = DashboardView.Small(text, Palette.TextMuted);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        public static TextBlock Glyph(string glyph, double size = 12, Brush brush = null)
        {
            return new TextBlock { Text = glyph, FontFamily = IconButton.Icons, FontSize = size, Foreground = brush ?? Palette.TextDim, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>Text link (accent colour) that behaves like a button.</summary>
        public static Pressable Link(string text, Action click, string glyph = null)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null) { var g = Glyph(glyph, 11, Palette.Accent); g.Margin = new Thickness(0, 1, 6, 0); p.Children.Add(g); }
            p.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Palette.Accent, VerticalAlignment = VerticalAlignment.Center });
            return new Pressable(text, click, 4) { Child = p, Padding = new Thickness(6, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Left };
        }

        /// <summary>Filled accent button for the main action of a panel.</summary>
        public static Pressable Primary(string text, Action click)
        {
            var b = new Pressable(text, click, 6) { Padding = new Thickness(14, 5, 14, 6), Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Palette.OnAccent } };
            b.SetRest(() => Palette.Accent);
            return b;
        }

        public static TextBox Field(string name, double width)
        {
            var t = new TextBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
            if (!Theme.Adopted) { t.Foreground = Palette.Text; t.CaretBrush = Palette.Text; t.Background = Brushes.Transparent; t.BorderBrush = Palette.CardEdge; t.Padding = new Thickness(2, 1, 2, 1); }
            AutomationProperties.SetName(t, name);
            return t;
        }

        /// <summary>Enter and leaving the box apply, Esc reverts.</summary>
        public static void Commits(TextBox t, Action commit, Action revert)
        {
            bool reverting = false;
            t.LostKeyboardFocus += (s, e) => { if (!reverting) commit(); };
            t.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { commit(); t.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Escape) { reverting = true; revert(); Keyboard.ClearFocus(); reverting = false; e.Handled = true; }
            };
            t.GotKeyboardFocus += (s, e) => t.SelectAll();
        }

        public static TextBox TextField(string name, string value, double width, Action<string> set)
        {
            var t = Field(name, width);
            t.Text = value ?? "";
            string last = t.Text;
            Commits(t, () => { if (t.Text != last) { last = t.Text; set(t.Text); } }, () => t.Text = last);
            return t;
        }

        public static CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 2, 16, 2), VerticalAlignment = VerticalAlignment.Center };
            if (!Theme.Adopted) c.Foreground = Palette.Text; // inside Fan Control the native check box style colours it
            RoutedEventHandler h = (s, e) => set(c.IsChecked == true);
            c.Checked += h; c.Unchecked += h;
            return c;
        }

        public static Segmented Choice(string name, string[] options, int selected, Action<int> changed)
        {
            var seg = new Segmented(options, changed, name);
            seg.Select(selected);
            seg.HorizontalAlignment = HorizontalAlignment.Left;
            return seg;
        }

        /// <summary>Two columns: muted labels left, controls right; rows stack under the label on narrow widths is not needed (labels are short).</summary>
        public sealed class Form
        {
            readonly Grid _grid = new Grid();
            int _row;
            public Form(double labelWidth = 104)
            {
                _grid.MaxWidth = 860; _grid.HorizontalAlignment = HorizontalAlignment.Left;
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            public FrameworkElement Element { get { return _grid; } }
            public void Add(string label, UIElement content, string help = null)
            {
                _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = DashboardView.Small(label); l.Margin = new Thickness(0, 8, 12, 8); l.VerticalAlignment = VerticalAlignment.Top; l.TextWrapping = TextWrapping.Wrap;
                Grid.SetRow(l, _row); _grid.Children.Add(l);
                var cell = new StackPanel { Margin = new Thickness(0, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
                var fe = content as FrameworkElement;
                if (fe != null && (!double.IsNaN(fe.Width) || fe is Segmented)) fe.HorizontalAlignment = HorizontalAlignment.Left;
                cell.Children.Add(content);
                if (help != null) { var h = Hint(help); h.Margin = new Thickness(0, 4, 0, 2); cell.Children.Add(h); }
                Grid.SetRow(cell, _row); Grid.SetColumn(cell, 1); _grid.Children.Add(cell);
                AutomationProperties.SetName(content, label);
                _row++;
            }
        }

        /// <summary>A panel with a title, a close button, content and a footer (optional destructive action left, Done right).</summary>
        public static FrameworkElement Panel(string title, UIElement content, Action done, string removeText = null, Action remove = null, string doneText = "Done")
        {
            var s = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var close = new IconButton(GlyphClose, "Close", done) { Width = 26, Height = 26, Margin = new Thickness(0, -4, -6, 0) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            var t = DashboardView.Label(title); t.FontSize = 14;
            head.Children.Add(t);
            s.Children.Add(head);
            s.Children.Add(content);
            var foot = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var ok = Primary(doneText, done);
            DockPanel.SetDock(ok, Dock.Right);
            foot.Children.Add(ok);
            if (remove != null)
            {
                var r = new Pressable(removeText, remove, 6) { Padding = new Thickness(8, 5, 8, 6), HorizontalAlignment = HorizontalAlignment.Left };
                var rp = new StackPanel { Orientation = Orientation.Horizontal };
                var g = Glyph(GlyphDelete, 12, Palette.Critical); g.Margin = new Thickness(0, 1, 6, 0);
                rp.Children.Add(g);
                rp.Children.Add(new TextBlock { Text = removeText, FontSize = 12, Foreground = Palette.Critical });
                r.Child = rp;
                foot.Children.Add(r);
            }
            else foot.Children.Add(new Border());
            s.Children.Add(foot);
            var card = DashboardView.Card(s);
            card.KeyDown += (o, e) => { if (e.Key == Key.Escape && !(e.OriginalSource is TextBox)) { done(); e.Handled = true; } };
            AutomationProperties.SetName(card, title);
            return card;
        }

        public static string Fmt(float v, string unit) { return Units.Format(v, unit); }

        public static double ParseNumber(string text)
        {
            double v;
            return double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, Inv, out v) && Math.Abs(v) < 1e7 ? v : double.NaN;
        }
    }

    /// <summary>
    /// Search-as-you-type list of every value the dashboard can show: the automatic ones first, then Fan Control's sensors
    /// grouped by hardware, each with its current value so similar names can be told apart.
    /// </summary>
    sealed class SensorPicker : Border
    {
        readonly Sampler _sampler;
        readonly Settings _settings;
        readonly Func<string, string, bool> _accept; // (ref, unit) => offer it?
        readonly string _current;
        readonly Action<string> _picked;
        readonly TextBox _search;
        readonly StackPanel _list = new StackPanel();
        readonly List<Tuple<string, TextBlock>> _values = new List<Tuple<string, TextBlock>>();
        readonly DispatcherTimer _timer;
        int _catalogCount = -1;

        public SensorPicker(Sampler sampler, Settings settings, string current, Func<string, string, bool> accept, Action<string> picked, Action cancel)
        {
            _sampler = sampler; _settings = settings; _current = current; _accept = accept ?? ((r, u) => true); _picked = picked;
            BorderBrush = Palette.CardEdge; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(6); Padding = new Thickness(10, 8, 6, 8);
            Margin = new Thickness(0, 6, 0, 4);
            AutomationProperties.SetName(this, "Choose a sensor");

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var close = new IconButton(Ui.GlyphClose, "Cancel", cancel) { Width = 26, Height = 26 };
            DockPanel.SetDock(close, Dock.Right);
            top.Children.Add(close);
            var icon = Ui.Glyph(Ui.GlyphSearch, 12); icon.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(icon, Dock.Left);
            top.Children.Add(icon);
            _search = Ui.Field("Search sensors", double.NaN);
            _search.HorizontalAlignment = HorizontalAlignment.Stretch;
            var placeholder = DashboardView.Small("Search by name or hardware, e.g. \"water\", \"nvme\", \"fan #3\"", Palette.TextMuted);
            placeholder.IsHitTestVisible = false; placeholder.Margin = new Thickness(4, 0, 0, 0);
            var searchHost = new Grid();
            searchHost.Children.Add(_search); searchHost.Children.Add(placeholder);
            _search.TextChanged += (s, e) => { placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Fill(); };
            _search.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { cancel(); e.Handled = true; }
                else if (e.Key == Key.Down || e.Key == Key.Enter)
                {
                    var first = FirstRow(); if (first == null) return;
                    if (e.Key == Key.Enter) first.Invoke(); else first.Focus();
                    e.Handled = true;
                }
            };
            top.Children.Add(searchHost);

            var scroll = new ScrollViewer { Content = _list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            var s2 = new StackPanel();
            s2.Children.Add(top);
            s2.Children.Add(scroll);
            Child = s2;

            _sampler.RequestDiscovery();
            _sampler.Peek();
            Fill();
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => { if (!IsVisible) return; _sampler.Peek(); if (_sampler.Catalog.Count != _catalogCount) Fill(); else UpdateValues(); };
            Loaded += (s, e) => { _timer.Start(); _search.Focus(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        Pressable FirstRow()
        {
            foreach (var c in _list.Children) { var p = c as Pressable; if (p != null) return p; }
            return null;
        }

        void Fill()
        {
            var catalog = _sampler.Catalog;
            _catalogCount = catalog.Count;
            _list.Children.Clear(); _values.Clear();
            var words = _search.Text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Func<string, bool> match = text => words.All(w => text.ToLowerInvariant().Contains(w));

            var autos = Refs.AutoRefs.Where(r => _accept(r, Refs.Unit(r, catalog))).Where(r =>
            {
                string found = AutoDetail(r);
                return match(Refs.Name(r, catalog) + " " + found + " automatic " + Refs.Group(r, catalog));
            }).ToList();
            if (autos.Count > 0)
            {
                Group("Found automatically", "Adapts to your hardware");
                foreach (var r in autos) Row(r, Refs.Name(r, catalog), AutoDetail(r));
            }
            var sensors = catalog.Where(Refs.Usable).Select(s => new { S = s, Ref = Refs.Sensor(s.Id) })
                                 .Where(x => _accept(x.Ref, Units.ForType(x.S.Type)))
                                 .Where(x => match(x.S.Name + " " + Refs.Source(x.S) + " " + Refs.Group(x.S) + " " + x.S.Type))
                                 .GroupBy(x => Refs.Group(x.S)).OrderBy(g => Array.IndexOf(Refs.Groups, g.Key));
            foreach (var g in sensors)
            {
                Group(g.Key == "Fan Control" ? "Fan Control (mix, custom and other sensors)" : g.Key, null);
                foreach (var x in g.OrderBy(x => Refs.Source(x.S), StringComparer.OrdinalIgnoreCase).ThenBy(x => x.S.Type).ThenBy(x => x.S.Name, StringComparer.OrdinalIgnoreCase))
                    Row(x.Ref, x.S.Name, Refs.Source(x.S));
            }
            if (_list.Children.Count == 0)
            {
                var t = DashboardView.Small(catalog.Count == 0 ? "Waiting for Fan Control's sensor list…" : "No sensor matches \"" + _search.Text + "\".", Palette.TextMuted);
                t.Margin = new Thickness(4, 8, 4, 8);
                _list.Children.Add(t);
            }
            UpdateValues();
        }

        string AutoDetail(string r)
        {
            string key = Refs.Key(r);
            if (key == "ram.temp") return _sampler.DimmNames.Count > 0 ? "from " + _sampler.DimmNames.Count + " module" + (_sampler.DimmNames.Count > 1 ? "s" : "") : "needs modules with a sensor (DDR5)";
            if (key == "ram.modules") return _sampler.DimmNames.Count > 0 ? string.Join(", ", _sampler.DimmNames) : "needs modules with a sensor (DDR5)";
            if (key == "gpu.load") return "busiest GPU engine";
            if (key == "ram.load" || key == "ram.used") return "Windows";
            string id = _sampler.AutomaticSensorId(key);
            var sensor = id == null ? null : _sampler.Catalog.FirstOrDefault(c => c.Id == id);
            if (sensor != null) return "now: " + sensor.Name + "  " + Refs.Source(sensor);
            return !float.IsNaN(_sampler.Get(key)) ? "" : _sampler.Discovered ? "not found on this PC" : "";
        }

        void Group(string title, string note)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, _list.Children.Count == 0 ? 2 : 12, 0, 4) };
            p.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Palette.TextMuted, VerticalAlignment = VerticalAlignment.Center });
            if (note != null) { var n = DashboardView.Small("  ·  " + note, Palette.TextMuted); n.FontSize = 10.5; p.Children.Add(n); }
            _list.Children.Add(p);
        }

        void Row(string r, string name, string source)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (r == _current) g.Children.Add(Ui.Glyph(Ui.GlyphCheck, 11, Palette.Accent));
            var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
            text.Inlines.Add(new System.Windows.Documents.Run(name) { Foreground = Palette.Text });
            if (!string.IsNullOrEmpty(source)) text.Inlines.Add(new System.Windows.Documents.Run("   " + source) { Foreground = Palette.TextMuted, FontSize = 11.5 });
            Grid.SetColumn(text, 1); g.Children.Add(text);
            var value = new TextBlock { FontSize = 12, Foreground = Palette.TextDim, Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 56, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(value, 2); g.Children.Add(value);
            _values.Add(Tuple.Create(r, value));
            var row = new Pressable(name + (string.IsNullOrEmpty(source) ? "" : ", " + source), () => _picked(r), 5) { Child = g, Padding = new Thickness(4, 4, 4, 4) };
            row.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Up && e.Key != Key.Down) return;
                int i = _list.Children.IndexOf(row);
                for (int j = i + (e.Key == Key.Down ? 1 : -1); j >= 0 && j < _list.Children.Count; j += e.Key == Key.Down ? 1 : -1)
                {
                    var p = _list.Children[j] as Pressable;
                    if (p != null) { p.Focus(); p.BringIntoView(); break; }
                }
                if (e.Key == Key.Up && _list.Children.OfType<Pressable>().FirstOrDefault() == row) _search.Focus();
                e.Handled = true;
            };
            _list.Children.Add(row);
        }

        void UpdateValues()
        {
            var catalog = _sampler.Catalog;
            foreach (var v in _values)
            {
                string r = v.Item1;
                float value = Refs.IsSensor(r) ? _sampler.PeekValue(Refs.SensorId(r)) : Refs.Key(r) == "ram.modules" ? float.NaN : _sampler.Get(Refs.Key(r));
                v.Item2.Text = float.IsNaN(value) ? (Refs.Key(r) == "ram.modules" ? "" : "–") : Units.Format(value, Refs.Unit(r, catalog));
            }
        }
    }

    /// <summary>A value slot in an editor: the chosen value with its current reading, and Change / Remove links.</summary>
    sealed class ValueSlot : StackPanel
    {
        public ValueSlot(DashboardView view, string current, string emptyText, Func<string, string, bool> accept, Action<string> set, bool removable, string addText = "Choose…")
        {
            var catalog = view.Sampler.Catalog;
            var row = new WrapPanel();
            if (current != null)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                chip.Children.Add(DashboardView.Swatch(Palette.ForRef(view.Settings, current)));
                chip.Children.Add(new TextBlock { Text = Refs.Name(current, catalog), FontSize = 12.5, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center });
                var v = view.Value(current);
                chip.Children.Add(DashboardView.Small("   " + (float.IsNaN(v) ? "" : Units.Format(v, Refs.Unit(current, catalog))), Palette.TextMuted));
                row.Children.Add(chip);
            }
            else
            {
                var e = DashboardView.Small(emptyText, Palette.TextMuted); e.Margin = new Thickness(0, 0, 10, 0);
                row.Children.Add(e);
            }
            SensorPicker picker = null;
            Action toggle = null;
            toggle = () =>
            {
                if (picker != null) { Children.Remove(picker); picker = null; return; }
                picker = new SensorPicker(view.Sampler, view.Settings, current, accept, r => { Children.Remove(picker); picker = null; set(r); }, () => toggle());
                Children.Add(picker);
            };
            row.Children.Add(Ui.Link(current == null ? addText : "Change…", () => toggle(), current == null && addText.StartsWith("Add", StringComparison.Ordinal) ? Ui.GlyphAdd : null));
            if (removable && current != null) row.Children.Add(Ui.Link("Remove", () => set(null)));
            Children.Add(row);
        }
    }

    /// <summary>Editor for one card.</summary>
    sealed class TileEditor
    {
        public static FrameworkElement Build(DashboardView view, TileConfig tile)
        {
            var settings = view.Settings;
            var catalog = view.Sampler.Catalog;
            Action save = view.SaveAndRefreshEditor;
            var f = new Ui.Form();
            f.Add("Title", Ui.TextField("Card title", tile.Title, 240, v => { if (!string.IsNullOrWhiteSpace(v)) { tile.Title = v.Trim(); settings.Save(); } }));

            if (tile.IsFans) return Ui.Panel("Edit card: " + tile.Title, FansEditor(view, f), view.CloseEditor, "Remove card", () => view.RemoveTile(tile));

            f.Add("Shows", new ValueSlot(view, tile.Main, "", null, r =>
            {
                string oldName = Refs.Name(tile.Main, catalog), oldShort = Refs.Short(tile.Main, catalog);
                if (tile.Title == oldName || tile.Title == oldShort || tile.Title == "Sensor") tile.Title = Refs.IsAuto(r) ? Refs.Short(r, catalog) : Refs.Name(r, catalog);
                settings.ReplaceMain(tile, r);
                save();
            }, false), "The big number. Charts that drew the old value draw this one instead.");

            f.Add("Colour", new ColorPicker("Card", Palette.ColorName(settings, tile.Main), Palette.DefaultColor(tile.Main), c =>
            {
                if (c == null) settings.Colors.Remove(tile.Main); else settings.Colors[tile.Main] = c;
                settings.Save();
            }), "Also the colour of this value's line in charts.");

            // warning limit, in the main value's unit
            string unit = Refs.Unit(tile.Main, catalog);
            bool temp = unit == "°C";
            var limitRow = new WrapPanel();
            var box = Ui.Field("Warn at", 64);
            box.TextAlignment = TextAlignment.Right;
            Func<string> show = () => double.IsNaN(tile.Limit) ? "" : (temp ? Units.ToDisplay(tile.Limit) : tile.Limit).ToString("0.##", CultureInfo.InvariantCulture);
            box.Text = show();
            Ui.Commits(box, () =>
            {
                double v = Ui.ParseNumber(box.Text);
                tile.Limit = string.IsNullOrWhiteSpace(box.Text) || double.IsNaN(v) ? double.NaN : temp ? Math.Round(Units.FromDisplay(v), 1) : v;
                box.Text = show();
                settings.Save();
            }, () => box.Text = show());
            limitRow.Children.Add(box);
            var u = DashboardView.Small(" " + Units.DisplayUnit(unit), Palette.TextDim); u.Margin = new Thickness(4, 0, 16, 0);
            limitRow.Children.Add(u);
            if (tile.Main == "auto:ram.temp")
            {
                float reported = view.Sampler.Get("ram.limit");
                limitRow.Children.Add(Ui.Check(float.IsNaN(reported) ? "Use the modules' own limit when they report one" : "Use the modules' own limit (" + Units.Format(reported, "°C") + ")",
                    tile.LimitFromModules, v => { tile.LimitFromModules = v; settings.Save(); }));
            }
            string limitHelp = temp && tile.Main == "auto:cpu.temp"
                ? "Your CPU's maximum temperature (Tjmax): about 95 °C for AMD Ryzen 7000/9000, 89 °C for Ryzen X3D, 100 °C for most Intel desktop CPUs. Empty = no warning."
                : "The card shows the headroom, turns amber when close and red above it; charts draw a dashed line. Empty = no warning.";
            f.Add("Warn at", limitRow, limitHelp);

            f.Add("Bar", new ValueSlot(view, tile.Bar, "", (r, ru) => ru == "%", r => { tile.Bar = r; tile.BarLabel = null; save(); }, true, "Add a bar"),
                "A percentage shown as a bar, e.g. a load or a fan's duty.");

            var details = new StackPanel();
            foreach (var d in tile.Details.ToList())
            {
                string detail = d;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var rm = new RowButton(Ui.GlyphClose, "Remove " + Refs.Name(detail, catalog), () => { tile.Details.Remove(detail); save(); });
                DockPanel.SetDock(rm, Dock.Left);
                row.Children.Add(rm);
                row.Children.Add(new TextBlock { Text = Refs.Name(detail, catalog), FontSize = 12.5, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                details.Children.Add(row);
            }
            if (tile.Details.Count < Settings.MaxDetails)
                details.Children.Add(new ValueSlot(view, null, "", (r, ru) => r != tile.Main && !tile.Details.Contains(r), r => { tile.Details.Add(r); save(); }, false, "Add a detail"));
            f.Add("Details", details, "Smaller values under the big number.");

            return Ui.Panel("Edit card: " + tile.Title, f.Element, view.CloseEditor, "Remove card", () => view.RemoveTile(tile));
        }

        static FrameworkElement FansEditor(DashboardView view, Ui.Form f)
        {
            var settings = view.Settings;
            var fans = view.Sampler.Fans;
            var list = new StackPanel();
            if (fans.Count == 0)
                list.Children.Add(DashboardView.Small(view.Sampler.ConfigName == null ? "Waiting for Fan Control's configuration…" : "No fan control is enabled in the current Fan Control profile.", Palette.TextMuted));
            var rows = FanOrder.Apply(fans, settings.Fans);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string id = r.Fan.ControlId;
                if (id == null) continue;
                string original = string.IsNullOrEmpty(r.Fan.Name) ? id : r.Fan.Name;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
                var moves = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
                var up = new RowButton(Ui.GlyphUp, "Move " + r.Name + " up", () => { FanOrder.Move(settings, fans, id, -1); view.SaveAndRefreshEditor(); }) { IsEnabled = i > 0 };
                var down = new RowButton(Ui.GlyphDown, "Move " + r.Name + " down", () => { FanOrder.Move(settings, fans, id, +1); view.SaveAndRefreshEditor(); }) { IsEnabled = i < rows.Count - 1 };
                moves.Children.Add(up); moves.Children.Add(down);
                DockPanel.SetDock(moves, Dock.Right);
                row.Children.Add(moves);
                var visible = Ui.Check("", !r.Hidden, v => { settings.Fan(id).Hidden = !v; settings.Save(); });
                visible.Margin = new Thickness(0, 0, 2, 0); visible.ToolTip = "Show this fan";
                AutomationProperties.SetName(visible, "Show " + original);
                DockPanel.SetDock(visible, Dock.Left);
                row.Children.Add(visible);
                var name = Ui.TextField("Name for " + original, r.Name, 200, v =>
                {
                    settings.Fan(id).Name = string.IsNullOrWhiteSpace(v) || v.Trim() == original ? null : v.Trim();
                    settings.Save();
                });
                DockPanel.SetDock(name, Dock.Left);
                row.Children.Add(name);
                var orig = DashboardView.Small(r.Name != original ? "in Fan Control: " + original : "", Palette.TextMuted);
                orig.Margin = new Thickness(10, 0, 0, 0); orig.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(orig);
                list.Children.Add(row);
            }
            f.Add("Fans", list, "Tick the fans to show, rename them for the dashboard (Fan Control keeps its names) and set their order.");
            return f.Element;
        }
    }

    /// <summary>Editor for one chart: title and lines.</summary>
    static class ChartEditor
    {
        public static FrameworkElement Build(DashboardView view, ChartConfig chart, bool pickFirst)
        {
            var settings = view.Settings;
            var catalog = view.Sampler.Catalog;
            var f = new Ui.Form();
            f.Add("Title", Ui.TextField("Chart title", chart.Title, 240, v => { if (!string.IsNullOrWhiteSpace(v)) { chart.Title = v.Trim(); settings.Save(); } }));
            string unit = chart.Lines.Count == 0 ? null : Refs.Unit(chart.Lines[0].Ref, catalog);

            var lines = new StackPanel();
            foreach (var l in chart.Lines.ToList())
            {
                var line = l;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var rm = new RowButton(Ui.GlyphClose, "Remove " + Refs.Name(line.Ref, catalog), () => { chart.Lines.Remove(line); view.SaveAndRefreshEditor(); });
                DockPanel.SetDock(rm, Dock.Left);
                row.Children.Add(rm);
                var colors = new ColorPicker(Refs.Name(line.Ref, catalog), Palette.ColorName(settings, line.Ref), Palette.DefaultColor(line.Ref), c =>
                {
                    if (c == null) settings.Colors.Remove(line.Ref); else settings.Colors[line.Ref] = c;
                    settings.Save();
                }, small: true);
                var name = new TextBlock { Text = Refs.Name(line.Ref, catalog), FontSize = 12.5, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis, Width = 220 };
                DockPanel.SetDock(name, Dock.Left);
                row.Children.Add(name);
                colors.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(colors);
                lines.Children.Add(row);
            }
            if (chart.Lines.Count < Settings.MaxLines)
            {
                var add = new ValueSlot(view, null, "",
                    (r, ru) => ru != "" && chart.Lines.All(x => x.Ref != r) && (unit == null || ru == unit),
                    r => { chart.Lines.Add(new LineConfig { Ref = r }); view.SaveAndRefreshEditor(); }, false, "Add a line");
                lines.Children.Add(add);
                if (pickFirst && chart.Lines.Count == 0) add.Loaded += (s, e) => { var link = FindLink(add); if (link != null) link.Invoke(); };
            }
            f.Add("Lines", lines, unit == null ? "The first line decides the chart's unit; all lines share it." : "All lines of a chart share one unit (" + Units.DisplayUnit(unit) + "). The colour is shared with the value's card.");
            return Ui.Panel("Edit chart: " + chart.Title, f.Element, view.CloseEditor, "Remove chart", () => view.RemoveChart(chart));
        }

        static Pressable FindLink(DependencyObject d)
        {
            if (d is Pressable) return (Pressable)d;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var r = FindLink(VisualTreeHelper.GetChild(d, i)); if (r != null) return r; }
            return null;
        }
    }

    /// <summary>"Add a card": a card for any sensor, or one of the ready-made cards that is not on the dashboard yet.</summary>
    static class NewCardPanel
    {
        public static FrameworkElement Build(DashboardView view)
        {
            var settings = view.Settings;
            var s = new StackPanel();
            var picker = new SensorPicker(view.Sampler, settings, null, null, r =>
            {
                var catalog = view.Sampler.Catalog;
                var tile = new TileConfig { Id = settings.NewId("t"), Title = Refs.IsAuto(r) ? Refs.Short(r, catalog) : Refs.Name(r, catalog), Main = r };
                if (Refs.Unit(r, catalog) == "%") tile.Bar = r;
                settings.Tiles.Add(tile);
                settings.Save();
                view.EditTile(tile.Id);
            }, view.CloseEditor);
            s.Children.Add(Ui.Hint("Pick the value the card shows. You can add a bar, details and a warning limit afterwards."));
            s.Children.Add(picker);

            var missing = Settings.PresetIds.Where(id => settings.Tile(id) == null).ToList();
            if (missing.Count > 0)
            {
                var label = Ui.Hint("Or bring back a ready-made card:"); label.Margin = new Thickness(0, 12, 0, 6);
                s.Children.Add(label);
                var presets = new WrapPanel();
                foreach (var id in missing)
                {
                    string pid = id;
                    var preset = Settings.Preset(pid);
                    var b = new TextButton(preset.Title, () => { settings.Tiles.Add(Settings.Preset(pid)); settings.Save(); view.CloseEditor(); });
                    b.Margin = new Thickness(0, 0, 8, 0);
                    presets.Children.Add(b);
                }
                s.Children.Add(presets);
            }
            return Ui.Panel("Add a card", s, view.CloseEditor, doneText: "Cancel");
        }
    }

    /// <summary>Options that apply to the whole dashboard.</summary>
    static class DisplayPanel
    {
        public static FrameworkElement Build(DashboardView view)
        {
            var s = view.Settings;
            var f = new Ui.Form(120);
            f.Add("Temperature", Ui.Choice("Temperature unit", new[] { "°C", "°F" }, s.Fahrenheit ? 1 : 0, i => { s.Fahrenheit = i == 1; s.Save(); view.RefreshEditor(); }));
            f.Add("Density", Ui.Choice("Density", new[] { "Comfortable", "Compact" }, s.Compact ? 1 : 0, i => { s.Compact = i == 1; s.Save(); }));
            f.Add("Charts", Ui.Choice("Chart arrangement", new[] { "Side by side", "Stacked" }, s.ChartsStacked ? 1 : 0, i => { s.ChartsStacked = i == 1; s.Save(); }),
                "Side by side when there is room (about 900 px).");
            int h = Array.IndexOf(Settings.Heights, s.ChartHeight);
            f.Add("Chart height", Ui.Choice("Chart height", new[] { "Small", "Medium", "Large" }, h < 0 ? 1 : h, i => { s.ChartHeight = Settings.Heights[i]; s.Save(); }));
            int sm = Math.Max(0, Array.IndexOf(Settings.SmoothingSteps, s.Smoothing));
            f.Add("Smooth lines", Ui.Choice("Smooth chart lines", new[] { "Off", "3 s", "10 s", "30 s" }, sm, i => { s.Smoothing = Settings.SmoothingSteps[i]; s.Save(); }),
                "Averages the chart lines; cards always show the latest value.");
            f.Add("Limit lines", Ui.Choice("Limit lines", new[] { "Show", "Hide" }, s.ShowLimits ? 0 : 1, i => { s.ShowLimits = i == 0; s.Save(); }),
                "Dashed lines in the charts for the cards' warning limits.");
            return Ui.Panel("Display options", f.Element, view.CloseEditor);
        }
    }
}
