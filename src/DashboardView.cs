using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FanControl.Dashboard
{
    /// <summary>
    /// The dashboard section at the top of Fan Control's Home page: cards and charts built from the settings, and a
    /// customize mode on the dashboard itself (click a card or chart to edit it, drag to reorder, eye to hide, add new ones).
    /// </summary>
    public sealed class DashboardView : Border
    {
        public const string Marker = "FanControl.Dashboard.View";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>Compact density (smaller paddings and numbers); read by the builders below.</summary>
        static bool _compact;

        readonly Sampler _sampler;
        readonly Settings _settings;
        readonly DispatcherTimer _timer;
        readonly TextBlock _subtitle = Small("", Palette.TextMuted);
        readonly StackPanel _body = new StackPanel();
        readonly Grid _header = new Grid { Margin = new Thickness(2, 0, 0, 10) };
        bool? _narrowHeader;
        readonly Segmented _range;
        readonly Pressable _customize;
        readonly IconButton _collapse;
        readonly ContentControl _editorHost = new ContentControl { Focusable = false };
        FrameworkElement _editBar;
        string _structure;

        // customize mode
        bool _editing;
        string _selected;          // tile or chart id being edited, "+card", "+chart:<id>", "display" or null
        Func<FrameworkElement> _editorFactory;

        // rebuilt by Build() whenever the structure (cards, charts, colours, units, density) changes
        ColumnsPanel _tiles, _charts;
        FrameworkElement _emptyHint, _addTile, _addChart;
        readonly Dictionary<string, EditFrame> _tileFrames = new Dictionary<string, EditFrame>();
        readonly Dictionary<string, EditFrame> _chartFrames = new Dictionary<string, EditFrame>();
        readonly List<Action> _refreshers = new List<Action>();
        readonly List<ChartCard> _chartCards = new List<ChartCard>();

        internal Sampler Sampler { get { return _sampler; } }
        internal Settings Settings { get { return _settings; } }

        public DashboardView(Sampler sampler) : this(sampler, new Settings()) { }

        public DashboardView(Sampler sampler, Settings settings)
        {
            _sampler = sampler;
            _settings = settings;
            Chart.Source = sampler;
            Tag = Marker;
            Margin = new Thickness(12, 8, 12, 10);
            HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(this, "Dashboard");
            Units.Fahrenheit = settings.Fahrenheit;

            // header: title, status line, history range, customize, collapse
            var header = _header;
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock { Text = "Dashboard", FontSize = Theme.HeaderSize, FontWeight = Theme.HeaderWeight, VerticalAlignment = VerticalAlignment.Center };
            if (Theme.HeaderStyle != null) title.Style = Theme.HeaderStyle; else title.Foreground = Palette.Text;
            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _range = new Segmented(RangeLabels(false), i => { _settings.RangeMinutes = Settings.Ranges[i]; _settings.Save(); }, "History length");
            _range.ToolTip = "How much history the charts show";
            _customize = HeaderButton(Ui.GlyphEdit, "Customize", "Choose and arrange cards and charts", () => SetEditing(!_editing));
            _collapse = new IconButton(Ui.GlyphUp, "Collapse the dashboard", () => { _settings.Collapsed = !_settings.Collapsed; _settings.Save(); });
            _customize.Margin = new Thickness(10, 0, 0, 0); _collapse.Margin = new Thickness(4, 0, 0, 0);
            tools.Children.Add(_range); tools.Children.Add(_customize); tools.Children.Add(_collapse);
            Grid.SetColumn(tools, 2);
            header.Children.Add(title);
            header.Children.Add(tools);
            _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            header.Children.Add(_subtitle);
            LayoutHeader(false);
            SizeChanged += (s, e) =>
            {
                LayoutHeader(e.NewSize.Width < 680);
                bool tight = e.NewSize.Width < 520;
                _range.SetLabels(RangeLabels(tight));
                ((TextBlock)((StackPanel)_customize.Child).Children[1]).Visibility = tight ? Visibility.Collapsed : Visibility.Visible;
            };

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(_body);
            Child = root;
            KeyDown += (s, e) =>
            {
                if (e.Key != Key.Escape || !_editing || e.OriginalSource is TextBox) return;
                if (_selected != null) CloseEditor(); else SetEditing(false);
                e.Handled = true;
            };

            _settings.Changed += ApplySettings;
            ApplySettings();

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => { if (Watched()) SafeRefresh(); };
            Loaded += (s, e) => { SafeRefresh(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        static string[] RangeLabels(bool tight)
        {
            return Settings.Ranges.Select(m => tight ? (m == 60 ? "1h" : m + "m") : (m == 60 ? "1 h" : m + " min")).ToArray();
        }

        static Pressable HeaderButton(string glyph, string text, string tip, Action click)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            var g = Ui.Glyph(glyph, 13, Palette.TextDim); g.Margin = new Thickness(0, 1, 6, 0);
            p.Children.Add(g);
            p.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center });
            return new Pressable(text, click, 6) { Child = p, Padding = new Thickness(9, 5, 10, 6), ToolTip = tip };
        }

        /// <summary>Status text next to the title, or on its own line when the view is narrow.</summary>
        void LayoutHeader(bool narrow)
        {
            if (_narrowHeader == narrow) return;
            _narrowHeader = narrow;
            Grid.SetRow(_subtitle, narrow ? 1 : 0); Grid.SetColumn(_subtitle, narrow ? 0 : 1); Grid.SetColumnSpan(_subtitle, narrow ? 3 : 1);
            _subtitle.Margin = narrow ? new Thickness(0, 4, 0, 0) : new Thickness(12, 2, 10, 0);
        }

        public void StopTimer() { _timer.Stop(); _settings.Changed -= ApplySettings; }

        /// <summary>Redraw only while someone can see it: on screen, window not minimized, input in the last 2 minutes
        /// (covers locked session and display off), and the memory watchdog has not tripped. History keeps recording.</summary>
        bool Watched()
        {
            try
            {
                if (Health.Tripped || !IsVisible) return false;
                var w = Window.GetWindow(this);
                if (w != null && (w.WindowState == WindowState.Minimized || !w.IsVisible)) return false;
                return Health.IdleSeconds() < 120;
            }
            catch { return true; }
        }

        /// <summary>Test hook for the stand-alone harness: range index, hover position, customize mode and an open editor.</summary>
        public void Debug(int range, double hover, bool editing, string editor) { Debug(range, hover, editing, editor, null); }

        /// <summary>Harness hook; <paramref name="drag"/> = "id:x,y" freezes a drag of that card at a pointer position in its panel.</summary>
        public void Debug(int range, double hover, bool editing, string editor, string drag)
        {
            if (drag != null)
            {
                SetEditing(true);
                UpdateLayout();
                var parts = drag.Split(':', ',');
                var tile = _settings.Tile(parts[0]);
                var frame = tile != null ? _tileFrames[tile.Id] : _chartFrames[parts[0]];
                var p = new Point(double.Parse(parts[1], Inv), double.Parse(parts[2], Inv));
                var panel = tile != null ? _tiles : _charts;
                var start = panel.SlotOf(frame);
                if (tile != null) BeginDrag(frame, new Point(start.Width / 2, 40), () => _tiles, _settings.Tiles, tile);
                else { var c = _settings.Chart(parts[0]); BeginDrag(frame, new Point(start.Width / 2, 40), () => _charts, _settings.Charts, c); }
                _drag.Move(p);
                return;
            }
            _settings.RangeMinutes = Settings.Ranges[Math.Max(0, Math.Min(Settings.Ranges.Length - 1, range))];
            _settings.Save();
            if (editing) SetEditing(true);
            if (editor == "card") Select("+card", () => NewCardPanel.Build(this));
            else if (editor == "display") Select("display", () => DisplayPanel.Build(this));
            else if (editor != null && _settings.Tile(editor) != null) EditTile(editor);
            else if (editor != null && _settings.Chart(editor) != null) EditChart(editor, false);
            Refresh();
            if (hover >= 0 && _chartCards.Count > 0) { var c = _chartCards[0].Chart; c.Hover(hover); OnHover(c, hover); }
        }

        // ---------------------------------------------------------------- customize mode

        void SetEditing(bool on)
        {
            _editing = on;
            if (on && _settings.Collapsed) _settings.Collapsed = false;
            if (!on) { _selected = null; _editorFactory = null; _editorHost.Content = null; }
            _settings.Save(); // applies (and keeps the file current)
            if (!on) _customize.Focus();
        }

        void Select(string id, Func<FrameworkElement> factory)
        {
            if (_selected == id) { CloseEditor(); return; }
            _selected = id; _editorFactory = factory;
            RefreshEditor();
            ApplySettings();
            var host = _editorHost.Content as FrameworkElement;
            if (host != null) host.BringIntoView();
        }

        internal void EditTile(string id) { _selected = null; Select(id, () => { var t = _settings.Tile(id); return t == null ? null : TileEditor.Build(this, t); }); }
        internal void EditChart(string id, bool pickFirst) { _selected = null; Select(id, () => { var c = _settings.Chart(id); return c == null ? null : ChartEditor.Build(this, c, pickFirst); }); }

        internal void CloseEditor()
        {
            _selected = null; _editorFactory = null; _editorHost.Content = null;
            ApplySettings();
        }

        /// <summary>Rebuilds the open editor (after changes that alter what it lists).</summary>
        internal void RefreshEditor()
        {
            var e = _editorFactory == null ? null : _editorFactory();
            if (e == null) { _selected = null; _editorFactory = null; }
            else e.Margin = new Thickness(0, 0, 0, Gap);
            _editorHost.Content = e;
        }

        internal void SaveAndRefreshEditor() { _settings.Save(); RefreshEditor(); }

        internal void RemoveTile(TileConfig tile) { _settings.Tiles.Remove(tile); CloseEditor(); _settings.Save(); }
        internal void RemoveChart(ChartConfig chart) { _settings.Charts.Remove(chart); CloseEditor(); _settings.Save(); }

        FrameworkElement EditBar()
        {
            var bar = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(HeaderButton(Ui.GlyphSettings, "Display", "Units, density, chart options", () => Select("display", () => DisplayPanel.Build(this))));
            Pressable reset = null;
            bool armed = false;
            var resetText = new TextBlock { Text = "Reset", FontSize = 12.5, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center };
            reset = new Pressable("Reset to the default layout", () =>
            {
                if (!armed)
                {
                    armed = true; resetText.Text = "Click again to reset"; resetText.Foreground = Palette.Critical;
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    t.Tick += (s, e) => { t.Stop(); armed = false; resetText.Text = "Reset"; resetText.Foreground = Palette.TextDim; };
                    t.Start();
                    return;
                }
                _settings.Reset(); CloseEditor(); _settings.Save();
            }, 6) { Child = resetText, Padding = new Thickness(10, 5, 10, 6), ToolTip = "Back to the default cards and charts. Your fan names are kept.", Margin = new Thickness(4, 0, 8, 0) };
            right.Children.Add(reset);
            right.Children.Add(Ui.Primary("Done", () => SetEditing(false)));
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label("Customize the dashboard"));
            var hint = Ui.Hint("Click a card or chart to change it, drag it to move it, the eye hides it. Esc leaves.");
            hint.Margin = new Thickness(0, 2, 12, 0);
            text.Children.Add(hint);
            bar.Children.Add(text);
            var card = Card(bar);
            card.Margin = new Thickness(0, 0, 0, Gap);
            AutomationProperties.SetName(card, "Customize the dashboard");
            return card;
        }

        // ---------------------------------------------------------------- applying settings

        /// <summary>Everything that needs new elements when it changes; visibility and order are applied without rebuilding.</summary>
        string Structure()
        {
            var s = _settings;
            return string.Join("|", new object[]
            {
                s.Compact, s.Fahrenheit, Palette.IsLight,
                string.Join(",", s.Colors.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value)),
                string.Join(";", s.Tiles.Select(t => string.Join("/", t.Id, t.Kind, t.Title, t.Main, t.Fallback, t.Bar, t.BarLabel, string.Join(",", t.Details), t.Limit.ToString("R", Inv), t.LimitFromModules))),
                string.Join(";", s.Charts.Select(c => c.Id + "/" + c.Title + "/" + string.Join(",", c.Lines.Select(l => l.Ref)))),
            });
        }

        /// <summary>Every value the cards and charts use (the sampler reads the Fan Control sensors among them).</summary>
        IEnumerable<string> UsedRefs()
        {
            foreach (var t in _settings.Tiles)
            {
                if (t.Main != null) yield return t.Main;
                if (t.Fallback != null) yield return t.Fallback;
                if (t.Bar != null) yield return t.Bar;
                foreach (var d in t.Details) yield return d;
            }
            foreach (var c in _settings.Charts) foreach (var l in c.Lines) yield return l.Ref;
        }

        void ApplySettings()
        {
            Units.Fahrenheit = _settings.Fahrenheit;
            _sampler.Configure(UsedRefs().ToList());
            string structure = Structure();
            if (structure != _structure)
            {
                _structure = structure;
                Build();
            }

            bool collapsed = _settings.Collapsed && !_editing;
            _body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            _collapse.Glyph = collapsed ? Ui.GlyphDown : Ui.GlyphUp;
            _collapse.Tip = collapsed ? "Expand the dashboard" : "Collapse the dashboard";
            _collapse.Visibility = _editing ? Visibility.Collapsed : Visibility.Visible;
            _customize.Visibility = _editing ? Visibility.Collapsed : Visibility.Visible;
            bool anyChart = _settings.Charts.Any(c => !c.Hidden);
            _range.Visibility = collapsed || (!anyChart && !_editing) ? Visibility.Collapsed : Visibility.Visible;
            _range.Select(Math.Max(0, Array.IndexOf(Settings.Ranges, _settings.RangeMinutes)));
            _editBar.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;

            // cards, in order; hidden ones only while customizing (dimmed)
            _tiles.Children.Clear();
            foreach (var t in _settings.Tiles)
            {
                EditFrame f;
                if (!_tileFrames.TryGetValue(t.Id, out f)) continue;
                f.SetState(_editing, t.Hidden, _selected == t.Id);
                if (!t.Hidden || _editing) _tiles.Children.Add(f);
            }
            if (_editing && _settings.Tiles.Count < Settings.MaxTiles) _tiles.Children.Add(_addTile);
            _tiles.Visibility = _tiles.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            _charts.Children.Clear();
            foreach (var c in _settings.Charts)
            {
                EditFrame f;
                if (!_chartFrames.TryGetValue(c.Id, out f)) continue;
                f.SetState(_editing, c.Hidden, _selected == c.Id);
                if (!c.Hidden || _editing) _charts.Children.Add(f);
            }
            if (_editing && _settings.Charts.Count < Settings.MaxCharts) _charts.Children.Add(_addChart);
            _charts.Visibility = _charts.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _charts.Margin = new Thickness(0, _tiles.Children.Count > 0 ? Gap : 0, 0, 0);
            _charts.ForceSingleColumn = _settings.ChartsStacked;
            _tiles.Animate = _charts.Animate = _editing;
            _charts.InvalidateMeasure();
            _emptyHint.Visibility = !_editing && _tiles.Children.Count == 0 && _charts.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int points = _settings.RangeMinutes * 60;
            foreach (var c in _chartCards) c.Apply(points);
            SafeRefresh();
        }

        static double Gap { get { return _compact ? 10 : 16; } }

        /// <summary>Creates every card and chart for the current settings.</summary>
        void Build()
        {
            _compact = _settings.Compact;
            _refreshers.Clear(); _tileFrames.Clear(); _chartFrames.Clear(); _chartCards.Clear();
            _tiles = new ColumnsPanel { Gap = Gap, MinColumnWidth = _compact ? 240 : 280 };
            _charts = new ColumnsPanel { Gap = Gap, MinColumnWidth = 440 };
            _header.Margin = new Thickness(2, 0, 0, _compact ? 6 : 10);

            foreach (var t in _settings.Tiles)
            {
                var tile = t;
                var card = tile.IsFans ? FanTile(tile) : SensorTile(tile);
                _tileFrames[tile.Id] = new EditFrame(card, tile.Title,
                    edit: () => EditTile(tile.Id),
                    toggle: () => { tile.Hidden = !tile.Hidden; _settings.Save(); },
                    move: d => { Settings.Move(_settings.Tiles, tile, _settings.Tiles.IndexOf(tile) + d); _settings.Save(); },
                    drag: _drag, begin: (frame, grab) => BeginDrag(frame, grab, () => _tiles, _settings.Tiles, tile));
            }
            foreach (var c in _settings.Charts)
            {
                var chart = c;
                var card = new ChartCard(this, chart, OnHover);
                _chartCards.Add(card);
                _chartFrames[chart.Id] = new EditFrame(card.Element, chart.Title,
                    edit: () => EditChart(chart.Id, false),
                    toggle: () => { chart.Hidden = !chart.Hidden; _settings.Save(); },
                    move: d => { Settings.Move(_settings.Charts, chart, _settings.Charts.IndexOf(chart) + d); _settings.Save(); },
                    drag: _drag, begin: (frame, grab) => BeginDrag(frame, grab, () => _charts, _settings.Charts, chart));
            }
            _addTile = AddButton("Add a card", "Any sensor, or a ready-made card", () => Select("+card", () => NewCardPanel.Build(this)), 120);
            _addChart = AddButton("Add a chart", "Lines from any sensors that share a unit", () =>
            {
                var chart = new ChartConfig { Id = _settings.NewId("c"), Title = "Chart" };
                _settings.Charts.Add(chart);
                _settings.Save();
                EditChart(chart.Id, true);
            }, 120);
            _emptyHint = EmptyHint();
            _editBar = EditBar();

            _body.Children.Clear();
            _body.Children.Add(_editBar);
            _body.Children.Add(_editorHost);
            _body.Children.Add(_emptyHint);
            _body.Children.Add(_tiles);
            _body.Children.Add(_charts);
        }

        readonly DragController _drag = new DragController();

        /// <summary>Starts dragging a card or chart: the list order changes live as it passes other items, and is saved on drop.</summary>
        void BeginDrag<T>(EditFrame frame, Point grab, Func<ColumnsPanel> panelOf, List<T> list, T item) where T : class
        {
            var panel = panelOf();
            Func<EditFrame, T> itemOf = f => list.FirstOrDefault(x => FrameOf(x) == f);
            int origin = list.IndexOf(item);
            Func<List<UIElement>> sequence = () =>
            {
                var seq = list.Select(x => (UIElement)FrameOf(x)).Where(x => x != null && panel.Children.Contains(x)).ToList();
                foreach (UIElement c in panel.Children) if (!seq.Contains(c)) seq.Add(c); // the "add" button stays last
                return seq;
            };
            _drag.Begin(frame, panel, grab, Scroller(),
                target => { var other = itemOf(target); if (other == null) return false; Settings.Move(list, item, list.IndexOf(other)); panel.Sequence = sequence(); return true; },
                cancel =>
                {
                    if (cancel) Settings.Move(list, item, origin);
                    panel.Sequence = cancel ? sequence() : null;
                    _settings.Save(); // applies the final order and keeps it
                    panel.Sequence = null;
                });
        }

        EditFrame FrameOf(object item)
        {
            EditFrame f = null;
            var t = item as TileConfig; if (t != null) _tileFrames.TryGetValue(t.Id, out f);
            var c = item as ChartConfig; if (c != null) _chartFrames.TryGetValue(c.Id, out f);
            return f;
        }

        /// <summary>Fan Control's page scroller around the dashboard (for scrolling while dragging near an edge).</summary>
        ScrollViewer Scroller()
        {
            DependencyObject d = this;
            while (d != null) { d = VisualTreeHelper.GetParent(d); var sv = d as ScrollViewer; if (sv != null) return sv; }
            return null;
        }

        FrameworkElement AddButton(string text, string hint, Action click, double minHeight)
        {
            var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var g = Ui.Glyph(Ui.GlyphAdd, 18, Palette.Accent); g.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(g);
            var t = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 2) };
            s.Children.Add(t);
            var h = Small(hint, Palette.TextMuted); h.HorizontalAlignment = HorizontalAlignment.Center; h.TextAlignment = TextAlignment.Center; h.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(h);
            var outline = new Rectangle { Stroke = Palette.CardEdge, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, RadiusX = 6, RadiusY = 6, IsHitTestVisible = false };
            var grid = new Grid { MinHeight = minHeight };
            grid.Children.Add(outline);
            grid.Children.Add(s);
            return new Pressable(text, click, 6) { Child = grid, Padding = new Thickness(0) };
        }

        FrameworkElement EmptyHint()
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            var t = Small("Every card and chart is hidden.", Palette.TextDim); t.Margin = new Thickness(0, 0, 12, 0);
            p.Children.Add(t);
            p.Children.Add(new TextButton("Customize", () => SetEditing(true)));
            return Card(p);
        }

        void OnHover(Chart source, double fraction)
        {
            foreach (var c in _chartCards)
                if (!ReferenceEquals(c.Chart, source)) c.Chart.Sync(fraction);
        }

        // ---------------------------------------------------------------- values

        /// <summary>The current value of a ref (NaN when it has none).</summary>
        internal float Value(string r) { return r == null ? float.NaN : _sampler.Get(Refs.Key(r)); }

        /// <summary>Card limit for a ref drawn in a chart: the visible card whose main value it is.</summary>
        internal double LimitFor(string r)
        {
            var tile = _settings.Tiles.FirstOrDefault(t => !t.IsFans && t.Main == r && !t.Hidden && (!double.IsNaN(t.Limit) || t.LimitFromModules));
            return tile == null ? double.NaN : Settings.EffectiveLimit(tile, _sampler.Get("ram.limit"));
        }

        internal string LimitLabel(string r)
        {
            var tile = _settings.Tiles.FirstOrDefault(t => t.Main == r);
            return tile == null ? "Limit" : tile.Title + (r == "auto:ram.temp" ? " warning" : " limit");
        }

        // ---------------------------------------------------------------- cards

        FrameworkElement SensorTile(TileConfig tile)
        {
            var t = tile;
            var catalog = _sampler.Catalog;
            var detail = Small("", Palette.TextMuted); var big = Big(); var status = Small("");
            var s = new StackPanel();
            s.Children.Add(Header(t.Title, detail, Palette.ForRef(_settings, t.Main)));
            s.Children.Add(big);
            s.Children.Add(status);

            var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            s.Children.Add(chips);

            Bar bar = null; TextBlock barText = null; FrameworkElement barRow = null;
            if (t.Bar != null)
            {
                bar = new Bar(Palette.ForRef(_settings, t.Bar)); barText = Value();
                barRow = LoadRow(bar, barText, t.BarLabel ?? Refs.Short(t.Bar, catalog), Refs.Name(t.Bar, catalog));
                s.Children.Add(barRow);
            }
            var charted = new HashSet<string>(_settings.Charts.SelectMany(c => c.Lines.Select(l => l.Ref)));
            string detailSignature = null;

            _refreshers.Add(() =>
            {
                var cat = _sampler.Catalog;
                string main = t.Main;
                float v = Value(main);
                // e.g. RAM without module temperatures: show the amount in use instead
                if (float.IsNaN(v) && t.Fallback != null && _sampler.Discovered && !float.IsNaN(Value(t.Fallback))) main = t.Fallback;
                v = Value(main);
                string unit = Refs.Unit(main, cat);
                detail.Text = DetailText(t, main);
                big.Inlines.Clear();
                big.Inlines.Add(new Run(Units.Number(v, unit)));
                if (unit.Length > 0) big.Inlines.Add(new Run(" " + Units.DisplayUnit(unit)) { FontSize = _compact ? 12 : 14, Foreground = Palette.TextDim });

                double limit = main == t.Main ? Settings.EffectiveLimit(t, _sampler.Get("ram.limit")) : double.NaN;
                if (float.IsNaN(v))
                    Status(status, !_sampler.Discovered ? "" : Refs.IsSensor(main) && cat.Count > 0 && cat.All(x => x.Id != Refs.SensorId(main)) ? "Sensor not found in Fan Control" : "No value from this sensor", Palette.TextMuted);
                else if (!double.IsNaN(limit)) Headroom(status, v, limit, main == "auto:ram.temp" ? "warning" : "limit", unit == "°C" ? (main == "auto:ram.temp" ? 3 : 5) : Math.Abs(limit) * 0.05, unit);
                else if (main == "auto:ram.used") Status(status, MemoryTotal(), Palette.TextDim);
                else Status(status, "", Palette.TextDim);
                status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

                // details: rebuilt when the set of shown values changes (modules can appear late), updated in place otherwise
                var items = DetailItems(t, main);
                string sig = string.Join("|", items.Select(i => i.Item1));
                if (sig != detailSignature)
                {
                    detailSignature = sig;
                    chips.Children.Clear();
                    foreach (var i in items)
                    {
                        var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), Tag = i.Item1 };
                        if (i.Item3 != null && charted.Contains(i.Item3)) chip.Children.Add(Swatch(Palette.ForRef(_settings, i.Item3)));
                        chip.Children.Add(Small(""));
                        chips.Children.Add(chip);
                    }
                }
                for (int k = 0; k < items.Count && k < chips.Children.Count; k++)
                {
                    var chip = (StackPanel)chips.Children[k];
                    ((TextBlock)chip.Children[chip.Children.Count - 1]).Text = items[k].Item2;
                }
                chips.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                if (bar != null)
                {
                    float b = Value(t.Bar);
                    bar.Value = b; barText.Text = Units.Format(b, "%");
                    barRow.Visibility = float.IsNaN(b) && _sampler.Discovered ? Visibility.Collapsed : Visibility.Visible;
                }
            });
            return Card(s);
        }

        /// <summary>The muted text next to a card's title: which sensor the main value comes from.</summary>
        string DetailText(TileConfig t, string main)
        {
            var cat = _sampler.Catalog;
            if (main != t.Main) return main == "auto:ram.used" ? "in use" : Refs.Short(main, cat);
            if (Refs.IsAuto(main))
            {
                string key = Refs.Key(main);
                if (key == "ram.temp") return _sampler.DimmNames.Count > 1 ? "hottest module" : "module";
                var n = _sampler.SensorName(key);
                return n == "GPU" ? "edge" : n ?? "";
            }
            string name = Refs.Name(main, cat);
            return name == t.Title ? "" : name;
        }

        string MemoryTotal()
        {
            float tot = _sampler.Get("ram.total");
            return float.IsNaN(tot) ? "" : "of " + tot.ToString("0", Inv) + " GB";
        }

        /// <summary>Detail chips: (identity, text, ref for the colour dot). "Modules" expands to one chip per memory module.</summary>
        List<Tuple<string, string, string>> DetailItems(TileConfig t, string shownMain)
        {
            var cat = _sampler.Catalog;
            var list = new List<Tuple<string, string, string>>();
            foreach (var d in t.Details)
            {
                if (d == shownMain) continue;
                if (d == "auto:ram.modules")
                {
                    var names = _sampler.DimmNames;
                    if (names.Count < 2 && t.Main == "auto:ram.temp") continue; // one module: the big number already is its temperature
                    for (int i = 0; i < names.Count; i++)
                    {
                        float m = _sampler.Get("ram.dimm" + i);
                        if (!float.IsNaN(m)) list.Add(Tuple.Create(d + i, names[i] + " " + Units.Format(m, "°C"), (string)null));
                    }
                    continue;
                }
                float v = Value(d);
                if (float.IsNaN(v) && _sampler.Discovered) continue;
                string unit = Refs.Unit(d, cat);
                string text = d == "auto:ram.used" ? "Used " + Units.Format(v, "GB") + (float.IsNaN(_sampler.Get("ram.total")) ? "" : " of " + _sampler.Get("ram.total").ToString("0", Inv) + " GB")
                            : Refs.Short(d, cat) + " " + Units.Format(v, unit);
                list.Add(Tuple.Create(d, text, d));
            }
            return list;
        }

        FrameworkElement FanTile(TileConfig tile)
        {
            var grid = new Grid();
            var empty = Small("", Palette.TextMuted);
            var rows = new List<Tuple<FanInfo, Bar, TextBlock, TextBlock>>();
            string signature = null;
            var s = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, _compact ? 4 : 8) };
            var unit = Small("duty · speed", Palette.TextMuted); DockPanel.SetDock(unit, Dock.Right);
            head.Children.Add(unit); head.Children.Add(Label(tile.Title));
            s.Children.Add(head);
            // names get the room they need (up to about half the card); the bar takes the rest
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 150 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 24 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            s.Children.Add(grid);
            empty.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(empty);
            _refreshers.Add(() =>
            {
                var fans = _sampler.Fans;
                var shown = FanOrder.Apply(fans, _settings.Fans).Where(r => !r.Hidden).ToList();
                string sig = string.Join("|", shown.Select(r => r.Fan.ControlId + "=" + r.Name + "=" + r.Fan.Curve));
                if (sig != signature)
                {
                    signature = sig;
                    grid.Children.Clear(); grid.RowDefinitions.Clear(); rows.Clear();
                    for (int i = 0; i < shown.Count; i++)
                    {
                        var f = shown[i];
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_compact ? 22 : 26) });
                        var name = new TextBlock { Text = f.Name, Foreground = Palette.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
                        name.ToolTip = string.IsNullOrEmpty(f.Fan.Curve) ? f.Name : f.Name + "\nCurve: " + f.Fan.Curve;
                        var bar = new Bar(Palette.Accent) { VerticalAlignment = VerticalAlignment.Center };
                        var pct = Value(); pct.HorizontalAlignment = HorizontalAlignment.Right;
                        var rpm = Small(""); rpm.HorizontalAlignment = HorizontalAlignment.Right; rpm.VerticalAlignment = VerticalAlignment.Center;
                        foreach (var cell in new[] { Tuple.Create((UIElement)name, 0), Tuple.Create((UIElement)bar, 1), Tuple.Create((UIElement)pct, 2), Tuple.Create((UIElement)rpm, 3) })
                        { Grid.SetRow(cell.Item1, i); Grid.SetColumn(cell.Item1, cell.Item2); grid.Children.Add(cell.Item1); }
                        rows.Add(Tuple.Create(f.Fan, bar, pct, rpm));
                    }
                    empty.Text = shown.Count > 0 ? "" : _sampler.ConfigName == null ? "Waiting for Fan Control's configuration…"
                        : fans.Count > 0 ? "All fans are hidden. Customize this card to show them."
                        : "No fan control is enabled in this profile. Enable one on Fan Control's Controls page to see it here.";
                    empty.Visibility = shown.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                }
                foreach (var r in rows)
                {
                    float duty = r.Item1.ControlId == null ? float.NaN : _sampler.Get("duty:" + r.Item1.ControlId);
                    float rpm = r.Item1.RpmId == null ? float.NaN : _sampler.Get("rpm:" + r.Item1.RpmId);
                    r.Item2.Value = duty; r.Item3.Text = Units.Format(duty, "%");
                    r.Item4.Text = r.Item1.RpmId == null ? "" : Units.Format(rpm, "rpm");
                }
            });
            return Card(s);
        }

        // ---------------------------------------------------------------- refresh

        void SafeRefresh()
        {
            try { Refresh(); } catch { /* never take Fan Control down */ }
        }

        bool _catalogReady;

        void Refresh()
        {
            // units and names of picked sensors are known once Fan Control's sensor list arrived: build again then
            if (_catalogReady != _sampler.Catalog.Count > 0) { _catalogReady = !_catalogReady; _structure = null; ApplySettings(); return; }
            foreach (var r in _refreshers) { try { r(); } catch { } }
            UpdateSubtitle();
            foreach (var c in _chartCards) c.SyncAvailability(_sampler);
            if (!_settings.Collapsed || _editing)
                foreach (var c in _chartCards) c.Chart.InvalidateVisual();
        }

        void UpdateSubtitle()
        {
            double stale = _sampler.LastSampleUtc == DateTime.MinValue ? double.PositiveInfinity : (DateTime.UtcNow - _sampler.LastSampleUtc).TotalSeconds;
            if (!_sampler.Discovered || stale > 10)
            {
                _subtitle.Text = !_sampler.Discovered ? "Connecting to Fan Control…" : string.Format(Inv, "No new values from Fan Control for {0:0} s", stale);
                _subtitle.Foreground = _sampler.Discovered ? Palette.Warn : Palette.TextMuted;
                return;
            }
            _subtitle.Foreground = Palette.TextMuted;
            if (_settings.Collapsed && !_editing)
            {
                // the main value of every visible card, e.g. "CPU 71 °C · GPU 49 °C · Water 31 °C"
                var bits = new List<string>();
                var cat = _sampler.Catalog;
                foreach (var t in _settings.Tiles.Where(t => !t.Hidden && !t.IsFans))
                {
                    float v = Value(t.Main);
                    if (!float.IsNaN(v)) bits.Add(t.Title + " " + Units.Format(v, Refs.Unit(t.Main, cat)));
                }
                _subtitle.Text = string.Join("   ·   ", bits);
                return;
            }
            var info = new List<string>();
            if (!string.IsNullOrEmpty(_sampler.ConfigName)) info.Add("Profile " + _sampler.ConfigName);
            string gpu = _sampler.GpuName ?? "";
            foreach (var prefix in new[] { "AMD Radeon ", "NVIDIA GeForce ", "Intel(R) " })
                if (gpu.StartsWith(prefix, StringComparison.Ordinal)) gpu = gpu.Substring(prefix.Length);
            if (gpu.Length > 0) info.Add(gpu);
            _subtitle.Text = string.Join("  ·  ", info);
        }

        /// <summary>"24 °C below the 95 °C limit", amber within <paramref name="warnWithin"/> (°C or the unit), red at or above it.</summary>
        static void Headroom(TextBlock t, float value, double limit, string word, double warnWithin, string unit)
        {
            bool temp = unit == "°C";
            double d = temp ? Math.Round(limit) - Math.Round(value) : limit - value;
            Func<double, string> amount = x => temp ? Math.Round(Units.DeltaToDisplay(x)).ToString("0", Inv) + " " + Units.Temp : Units.Format((float)x, unit);
            string lim = Units.Format((float)limit, unit);
            if (d > warnWithin) Status(t, amount(d) + " below the " + lim + " " + word, Palette.TextDim);
            else if (d > 0) Status(t, amount(d) + " below the " + lim + " " + word, Palette.Warn);
            else if (d == 0) Status(t, "At the " + lim + " " + word, Palette.Critical);
            else Status(t, amount(-d) + " above the " + lim + " " + word, Palette.Critical);
        }

        static void Status(TextBlock t, string text, Brush b) { t.Text = text; t.Foreground = b; }

        // ---------------------------------------------------------------- small building blocks

        internal static FrameworkElement Card(UIElement child)
        {
            var padding = _compact ? new Thickness(12, 8, 12, 10) : new Thickness(16, 12, 16, 14);
            if (Theme.CardType != null)
            {
                try
                {
                    var c = (ContentControl)Activator.CreateInstance(Theme.CardType);
                    if (Theme.CardStyle != null) c.Style = Theme.CardStyle;
                    // the native style sizes cards for the small fixed tiles; ours fill their column
                    c.Width = double.NaN; c.Height = double.NaN; c.MinWidth = 0; c.MaxWidth = double.PositiveInfinity;
                    c.HorizontalAlignment = HorizontalAlignment.Stretch; c.VerticalAlignment = VerticalAlignment.Stretch;
                    c.Margin = new Thickness(0);
                    if (Theme.CardBackground != null) c.Background = Theme.CardBackground;
                    c.Padding = padding;
                    c.Content = child;
                    return c;
                }
                catch { }
            }
            return new Border { Background = Palette.Card, CornerRadius = new CornerRadius(4), Padding = padding, Child = child };
        }

        static FrameworkElement Header(string text, TextBlock detail, Brush swatch)
        {
            var p = new DockPanel { Margin = new Thickness(0, 0, 0, 2), LastChildFill = true };
            var sw = Swatch(swatch); DockPanel.SetDock(sw, Dock.Left); p.Children.Add(sw);
            var label = Label(text); label.TextTrimming = TextTrimming.CharacterEllipsis; DockPanel.SetDock(label, Dock.Left); p.Children.Add(label);
            detail.Margin = new Thickness(8, 1, 0, 0); detail.TextTrimming = TextTrimming.CharacterEllipsis;
            p.Children.Add(detail);
            return p;
        }

        internal static TextBlock Label(string text) { return new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center }; }
        internal static TextBlock Small(string text, Brush b = null) { return new TextBlock { Text = text, FontSize = 12, Foreground = b ?? Palette.TextDim, VerticalAlignment = VerticalAlignment.Center }; }
        static TextBlock Value() { return new TextBlock { FontSize = 12.5, Foreground = Palette.Text, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right }; }
        static TextBlock Big() { return new TextBlock { FontSize = _compact ? 21 : 26, FontWeight = FontWeights.Normal, Foreground = Palette.Text, Margin = new Thickness(0, 0, 0, _compact ? 0 : 2) }; }

        internal static FrameworkElement Swatch(Brush b, bool hollow = false)
        {
            return new Ellipse { Width = 8, Height = 8, Fill = hollow ? null : b, Stroke = hollow ? b : null, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        }

        static FrameworkElement LoadRow(Bar bar, TextBlock text, string label, string tip)
        {
            var g = new Grid { Margin = new Thickness(0, _compact ? 6 : 10, 0, 0), Height = 18, Background = Brushes.Transparent, ToolTip = tip };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 42 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            var l = Small(label); l.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(l, 0); g.Children.Add(l);
            bar.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bar, 1); g.Children.Add(bar);
            Grid.SetColumn(text, 2); g.Children.Add(text);
            return g;
        }
    }

    /// <summary>
    /// Wraps a card or chart. In customize mode it shows a dashed outline and a small toolbar (grip, arrows, hide, edit);
    /// clicking the card opens its editor. Outside customize mode it is just the card.
    /// </summary>
    sealed class EditFrame : Grid
    {
        readonly FrameworkElement _content;
        readonly Rectangle _outline = new Rectangle { RadiusX = 7, RadiusY = 7, IsHitTestVisible = false, StrokeThickness = 1.5 };
        readonly FrameworkElement _strip;
        readonly Border _shield;
        readonly TextBlock _hiddenNote;
        readonly RowButton _eye;
        readonly Action _edit;
        readonly DragController _drag;
        readonly Action<EditFrame, Point> _begin;
        bool _editing, _selected, _pressed, _dragging;
        Point _press;
        const double DragThreshold = 5;

        public EditFrame(FrameworkElement content, string name, Action edit, Action toggle, Action<int> move, DragController drag, Action<EditFrame, Point> begin)
        {
            _content = content; _edit = edit; _drag = drag; _begin = begin;
            Focusable = true; FocusVisualStyle = null;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // strip above the card (customize mode only): grip, "Hidden", arrows, hide, edit
            var strip = new DockPanel { Margin = new Thickness(4, 2, 4, 2), Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Drag to move" };
            var grip = new TextBlock { Text = "\u2630", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 12, Foreground = Palette.TextDim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            DockPanel.SetDock(grip, Dock.Left);
            strip.Children.Add(grip);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Arrow };
            tools.Children.Add(new RowButton(Ui.GlyphLeft, "Move " + name + " earlier", () => move(-1)));
            tools.Children.Add(new RowButton(Ui.GlyphRight, "Move " + name + " later", () => move(+1)));
            _eye = new RowButton(Ui.GlyphEye, "Hide " + name, toggle);
            tools.Children.Add(_eye);
            tools.Children.Add(new RowButton(Ui.GlyphEdit, "Edit " + name, edit));
            DockPanel.SetDock(tools, Dock.Right);
            strip.Children.Add(tools);
            _hiddenNote = DashboardView.Small("Hidden", Palette.TextMuted);
            strip.Children.Add(_hiddenNote);
            _strip = strip;
            Children.Add(strip);

            Grid.SetRow(content, 1);
            Children.Add(content);
            // transparent layer over the card: a click edits it, a drag (from anywhere on the card or the strip) moves it
            _shield = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to edit, drag to move" };
            Grid.SetRow(_shield, 1);
            Children.Add(_shield);
            foreach (var surface in new UIElement[] { _shield, strip }) Wire(surface, surface == _shield);
            Grid.SetRowSpan(_outline, 2);
            _outline.Margin = new Thickness(-1);
            Children.Add(_outline);
            MouseEnter += (s, e) => Paint();
            MouseLeave += (s, e) => Paint();
            KeyDown += (s, e) => { if (e.Key == Key.Escape && _dragging) { _dragging = false; _pressed = false; Mouse.Capture(null); _drag.End(true); e.Handled = true; } };
            AutomationProperties.SetName(this, name);
            SetState(false, false, false);
        }

        void Wire(UIElement surface, bool clickEdits)
        {
            surface.MouseLeftButtonDown += (s, e) =>
            {
                if (!_editing || e.OriginalSource is TextBlock && ((TextBlock)e.OriginalSource).FontFamily == IconButton.Icons) return;
                _pressed = true; _press = e.GetPosition(this);
                surface.CaptureMouse();
                e.Handled = true;
            };
            surface.MouseMove += (s, e) =>
            {
                if (!_pressed) return;
                if (!_dragging)
                {
                    var p = e.GetPosition(this);
                    if (Math.Abs(p.X - _press.X) < DragThreshold && Math.Abs(p.Y - _press.Y) < DragThreshold) return;
                    _dragging = true;
                    Focus(); // so Esc can cancel
                    _begin(this, _press);
                }
                _drag.Move();
            };
            surface.MouseLeftButtonUp += (s, e) =>
            {
                if (!_pressed) return;
                _pressed = false;
                bool dragged = _dragging;
                _dragging = false;
                surface.ReleaseMouseCapture();
                if (dragged) _drag.End(false);
                else if (clickEdits) _edit();
                e.Handled = true;
            };
            surface.LostMouseCapture += (s, e) =>
            {
                if (!_dragging) { _pressed = false; return; }
                _dragging = false; _pressed = false;
                _drag.End(false);
            };
        }

        public void SetState(bool editing, bool hidden, bool selected)
        {
            bool entering = editing && !_editing;
            _editing = editing; _selected = selected;
            _shield.Visibility = _outline.Visibility = _strip.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            _hiddenNote.Visibility = editing && hidden ? Visibility.Visible : Visibility.Collapsed;
            _content.Opacity = editing && hidden ? 0.4 : 1;
            ((TextBlock)_eye.Child).Text = hidden ? Ui.GlyphEyeOff : Ui.GlyphEye;
            _eye.Tip(hidden ? "Show" : "Hide");
            Paint();
            if (entering)
            {
                // the edit chrome fades in instead of popping
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160)));
                _strip.BeginAnimation(OpacityProperty, fade);
                _outline.BeginAnimation(OpacityProperty, fade);
            }
        }

        void Paint()
        {
            bool hot = _editing && (IsMouseOver || _dragging);
            _outline.Stroke = _selected || hot ? Palette.Accent : Palette.CardEdge;
            _outline.StrokeDashArray = _selected || hot ? null : new DoubleCollection { 4, 3 };
            _outline.StrokeThickness = _selected ? 2 : 1.5;
        }
    }

    /// <summary>
    /// Moves a card with the pointer: lifted (shadow, slightly larger) above the others while the rest make room, then
    /// settles into its new slot. Scrolls Fan Control's page when the pointer nears its top or bottom edge.
    /// </summary>
    sealed class DragController
    {
        EditFrame _frame;
        ColumnsPanel _panel;
        ScrollViewer _scroll;
        Point _grab;
        Func<EditFrame, bool> _moveTo;
        Action<bool> _done;
        TranslateTransform _t;
        ScaleTransform _s;
        System.Windows.Threading.DispatcherTimer _timer;

        public bool Active { get { return _frame != null; } }

        public void Begin(EditFrame frame, ColumnsPanel panel, Point grab, ScrollViewer scroll, Func<EditFrame, bool> moveTo, Action<bool> done)
        {
            if (_frame != null) End(true);
            _frame = frame; _panel = panel; _grab = grab; _scroll = scroll; _moveTo = moveTo; _done = done;
            _t = new TranslateTransform();
            _s = new ScaleTransform(1, 1, frame.ActualWidth / 2, frame.ActualHeight / 2);
            var group = new TransformGroup(); group.Children.Add(_s); group.Children.Add(_t);
            frame.RenderTransform = group;
            Panel.SetZIndex(frame, 100);
            frame.Background = Palette.Card; // lifted: opaque, so the placeholder does not show through
            frame.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = 0.35, Direction = 270 };
            var lift = new System.Windows.Media.Animation.DoubleAnimation(1, 1.025, new Duration(TimeSpan.FromMilliseconds(120)));
            _s.BeginAnimation(ScaleTransform.ScaleXProperty, lift);
            _s.BeginAnimation(ScaleTransform.ScaleYProperty, lift);
            panel.Animate = true;
            panel.Dragged = frame;
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (s, e) => AutoScroll();
            if (Mouse.LeftButton == MouseButtonState.Pressed) { _timer.Start(); Move(); }
        }

        /// <summary>Follows the pointer and moves the card to the slot under it.</summary>
        public void Move() { if (_frame != null) Move(Mouse.GetPosition(_panel)); }

        /// <summary>Follows a pointer position in panel coordinates (the harness uses this directly).</summary>
        internal void Move(Point p)
        {
            if (_frame == null) return;
            foreach (UIElement c in _panel.Children)
            {
                var other = c as EditFrame;
                if (other == null || other == _frame) continue;
                if (!_panel.SlotOf(other).Contains(p)) continue;
                if (_moveTo(other)) _panel.UpdateLayout();
                break;
            }
            var slot = _panel.SlotOf(_frame);
            _t.X = p.X - _grab.X - slot.X;
            _t.Y = p.Y - _grab.Y - slot.Y;
        }

        void AutoScroll()
        {
            if (_frame == null || _scroll == null) return;
            var p = Mouse.GetPosition(_scroll);
            const double edge = 56;
            double dy = p.Y < edge ? -(edge - p.Y) / 3 : p.Y > _scroll.ViewportHeight - edge ? (p.Y - (_scroll.ViewportHeight - edge)) / 3 : 0;
            if (Math.Abs(dy) < 0.5) return;
            _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + dy);
            _scroll.UpdateLayout();
            Move();
        }

        /// <summary>Drops the card (or puts it back on Esc) and lets it glide into its slot.</summary>
        public void End(bool cancel)
        {
            if (_frame == null) return;
            var frame = _frame; var panel = _panel; var t = _t; var s = _s; var done = _done;
            _frame = null; _panel = null;
            if (_timer != null) { _timer.Stop(); _timer = null; }
            // where the card is on screen now, relative to the slot it will end up in
            var before = t.Transform(new Point(0, 0));
            var slotBefore = panel.SlotOf(frame);
            done(cancel);
            panel.UpdateLayout();
            var slotAfter = panel.SlotOf(frame);
            double fromX = before.X + slotBefore.X - slotAfter.X, fromY = before.Y + slotBefore.Y - slotAfter.Y;
            panel.Dragged = null;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var time = new Duration(TimeSpan.FromMilliseconds(180));
            var settle = new TranslateTransform(fromX, fromY);
            var scale = new ScaleTransform(s.ScaleX, s.ScaleY, s.CenterX, s.CenterY);
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(settle);
            frame.RenderTransform = group;
            var x = new System.Windows.Media.Animation.DoubleAnimation(fromX, 0, time) { EasingFunction = ease };
            x.Completed += (o, e) =>
            {
                frame.RenderTransform = new TranslateTransform();
                frame.Effect = null;
                frame.Background = null;
                Panel.SetZIndex(frame, 0);
            };
            settle.BeginAnimation(TranslateTransform.XProperty, x);
            settle.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(fromY, 0, time) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1, time));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(1, time));
        }
    }

    /// <summary>A chart in its own card: title, unit, clickable legend.</summary>
    sealed class ChartCard
    {
        public readonly Chart Chart;
        public readonly FrameworkElement Element;
        readonly Settings _settings;
        readonly ChartConfig _config;
        readonly WrapPanel _legend = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly HashSet<string> _seen = new HashSet<string>();
        bool _discovered;
        string _legendSignature;

        public ChartCard(DashboardView view, ChartConfig config, Action<Chart, double> hover)
        {
            _settings = view.Settings; _config = config;
            var catalog = view.Sampler.Catalog;
            string unit = config.Lines.Count == 0 ? "" : Refs.Unit(config.Lines[0].Ref, catalog);
            Chart = new Chart(_settings) { Margin = new Thickness(0, 10, 0, 0), Unit = unit };
            Chart.HoverChanged = hover;
            Chart.IsAvailable = r => !_discovered || _seen.Contains(r);
            Chart.IsHidden = r => { var l = _config.Lines.FirstOrDefault(x => x.Ref == r); return l != null && l.Hidden; };
            Chart.SetSeries(config.Lines.Where(l => Refs.Unit(l.Ref, catalog) == unit).Select(l =>
            {
                string r = l.Ref;
                return new SeriesDef
                {
                    Ref = r, Key = Refs.Key(r), Name = Refs.IsAuto(r) ? ShortAutoName(r, catalog) : Refs.Name(r, catalog),
                    Short = Refs.IsAuto(r) ? ShortAutoName(r, catalog).Replace("GPU hotspot", "Hotspot") : Refs.Short(r, catalog), Unit = unit,
                    Brush = Palette.ForRef(_settings, r), Limit = () => view.LimitFor(r), LimitLabel = view.LimitLabel(r),
                };
            }).ToArray());
            AutomationProperties.SetName(Chart, config.Title + " chart");
            var head = new DockPanel { LastChildFill = true };
            var titles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
            titles.Children.Add(DashboardView.Label(config.Title));
            var u = DashboardView.Small(Units.DisplayUnit(unit), Palette.TextMuted); u.Margin = new Thickness(6, 1, 0, 0);
            titles.Children.Add(u);
            DockPanel.SetDock(titles, Dock.Left);
            head.Children.Add(titles);
            _legend.Margin = new Thickness(12, 0, 0, 0);
            head.Children.Add(_legend);
            var s = new StackPanel();
            s.Children.Add(head);
            s.Children.Add(Chart);
            Element = DashboardView.Card(s);
        }

        /// <summary>Legend names: "CPU", "GPU hotspot" rather than the long names used in editors.</summary>
        static string ShortAutoName(string r, IList<SensorInfo> catalog)
        {
            switch (r)
            {
                case "auto:gpu.hot": return "GPU hotspot";
                case "auto:gpu.vram": return "VRAM";
                case "auto:cpu.load": return "CPU";
                case "auto:gpu.load": return "GPU";
                case "auto:ram.load": case "auto:ram.temp": return "RAM";
                case "auto:cpu.power": return "CPU power";
                case "auto:ram.used": return "RAM used";
                default: return Refs.Short(r, catalog);
            }
        }

        public void Apply(int points)
        {
            Chart.Points = points;
            Chart.Height = _settings.ChartHeight;
            UpdateLegend();
            Chart.InvalidateVisual();
        }

        string Signature() { return _discovered + ":" + string.Join(",", Chart.AllSeries.Where(d => _seen.Contains(d.Ref)).Select(d => d.Ref + (_config.Lines.Any(l => l.Ref == d.Ref && l.Hidden) ? "-" : ""))); }

        /// <summary>Lists only lines that have (had) data, once Fan Control's sensors are known.</summary>
        public void SyncAvailability(Sampler sampler)
        {
            _discovered = sampler.Discovered;
            foreach (var d in Chart.AllSeries)
                if (!float.IsNaN(sampler.Get(d.Key))) _seen.Add(d.Ref);
            if (Signature() != _legendSignature) UpdateLegend();
        }

        void UpdateLegend()
        {
            _legendSignature = Signature();
            _legend.Children.Clear();
            foreach (var s in Chart.AllSeries)
            {
                var def = s;
                if (_discovered && !_seen.Contains(def.Ref)) continue;
                var line = _config.Lines.FirstOrDefault(l => l.Ref == def.Ref);
                bool hidden = line != null && line.Hidden;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(DashboardView.Swatch(def.Brush, hidden));
                var label = DashboardView.Small(def.Name, hidden ? Palette.TextMuted : Palette.TextDim);
                if (hidden) label.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(label);
                var item = new Pressable((hidden ? "Show " : "Hide ") + def.Name, () => { if (line != null) { line.Hidden = !line.Hidden; _settings.Save(); UpdateLegend(); } }, 5)
                {
                    Child = row, Padding = new Thickness(6, 2, 6, 3), Margin = new Thickness(0, 1, 2, 1),
                    ToolTip = (hidden ? "Show " : "Hide ") + def.Name,
                };
                _legend.Children.Add(item);
            }
        }
    }
}
