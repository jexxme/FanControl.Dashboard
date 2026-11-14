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
    /// <summary>The dashboard section that is placed at the top of Fan Control's Home page.</summary>
    public sealed class DashboardView : Border
    {
        public const string Marker = "FanControl.Dashboard.View";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string GlyphSettings = "", GlyphCollapse = "", GlyphExpand = "";
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
        readonly IconButton _gear, _collapse;
        readonly SettingsPanel _panel;
        bool _settingsOpen;
        string _structure;

        // rebuilt by Build() whenever the structure (cards, colours, units, density) changes
        ColumnsPanel _cards, _charts;
        FrameworkElement _emptyHint;
        readonly Dictionary<string, FrameworkElement> _cardViews = new Dictionary<string, FrameworkElement>();
        readonly List<Action> _refreshers = new List<Action>();
        ChartCard _tempCard, _loadCard, _singleCard;

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

            // header: title, status line, history range, settings, collapse
            var header = _header;
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock { Text = "Dashboard", FontSize = Theme.HeaderSize, FontWeight = Theme.HeaderWeight, VerticalAlignment = VerticalAlignment.Center };
            if (Theme.HeaderStyle != null) title.Style = Theme.HeaderStyle; else title.Foreground = Palette.Text;
            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _range = new Segmented(Settings.Ranges.Select(m => m == 60 ? "1 h" : m + " min").ToArray(), i => { _settings.RangeMinutes = Settings.Ranges[i]; _settings.Save(); }, "History length");
            _range.ToolTip = "How much history the charts show";
            _gear = new IconButton(GlyphSettings, "Dashboard settings", ToggleSettings);
            _collapse = new IconButton(GlyphCollapse, "Collapse the dashboard", () => { _settings.Collapsed = !_settings.Collapsed; _settings.Save(); });
            _gear.Margin = new Thickness(10, 0, 0, 0); _collapse.Margin = new Thickness(2, 0, 0, 0);
            tools.Children.Add(_range); tools.Children.Add(_gear); tools.Children.Add(_collapse);
            Grid.SetColumn(tools, 2);
            header.Children.Add(title);
            header.Children.Add(tools);
            _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            header.Children.Add(_subtitle);
            LayoutHeader(false);
            SizeChanged += (s, e) =>
            {
                LayoutHeader(e.NewSize.Width < 680);
                _range.SetLabels(Settings.Ranges.Select(m => e.NewSize.Width < 480 ? (m == 60 ? "1h" : m + "m") : (m == 60 ? "1 h" : m + " min")).ToArray());
            };

            _panel = new SettingsPanel(_settings, _sampler, ToggleSettings);
            _panel.Element.Margin = new Thickness(0, 0, 0, 16);

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(_body);
            Child = root;

            _settings.Changed += ApplySettings;
            ApplySettings();

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => { if (Watched()) SafeRefresh(); };
            Loaded += (s, e) => { SafeRefresh(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
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

        /// <summary>Test hook for the stand-alone harness.</summary>
        public void Debug(int layout, int range, double hover, bool settingsOpen, int tab = 0)
        {
            _settings.Layout = (ChartLayout)Math.Max(0, Math.Min(2, layout));
            _settings.RangeMinutes = Settings.Ranges[Math.Max(0, Math.Min(Settings.Ranges.Length - 1, range))];
            _settingsOpen = settingsOpen;
            _settings.Save();
            if (settingsOpen) _panel.Tab = tab;
            Refresh();
            if (hover >= 0) { var c = _settings.Layout == ChartLayout.Single ? _singleCard.Chart : _tempCard.Chart; c.Hover(hover); OnHover(c, hover); }
        }

        // ---------- settings ----------

        void ToggleSettings()
        {
            _settingsOpen = !_settingsOpen;
            if (_settingsOpen) _panel.ShowTab();
            if (_settingsOpen && _settings.Collapsed) { _settings.Collapsed = false; _settings.Save(); }
            else ApplySettings();
            if (!_settingsOpen) _gear.Focus();
        }

        /// <summary>Everything that needs new elements when it changes; other settings only toggle or reorder.</summary>
        string Structure()
        {
            var s = _settings;
            return string.Join("|", new object[]
            {
                s.Compact, s.Fahrenheit, s.ShowCpuPower, s.ShowGpuDetails, s.ShowRamUsage, s.ShowRamModules, Palette.IsLight,
                string.Join(",", s.Colors.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value)),
                string.Join(",", s.Custom.Select(c => c.Id + "=" + c.Name + "/" + c.SensorId + "/" + c.Unit + "/" + c.Chart + "/" + c.Limit.ToString("R", Inv) + "/" + c.Color)),
            });
        }

        void ApplySettings()
        {
            Units.Fahrenheit = _settings.Fahrenheit;
            _sampler.Configure(_settings.Sensors, _settings.Custom.ToDictionary(c => c.Key, c => c.SensorId));
            string structure = Structure();
            if (structure != _structure)
            {
                bool unitChanged = _structure != null && _structure.Split('|')[1] != structure.Split('|')[1];
                _structure = structure;
                Build();
                if (unitChanged && _settingsOpen) _panel.ShowTab(); // limit boxes show the new unit
            }

            bool collapsed = _settings.Collapsed;
            _body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            _collapse.Glyph = collapsed ? GlyphExpand : GlyphCollapse;
            _collapse.Tip = collapsed ? "Expand the dashboard" : "Collapse the dashboard";
            _range.Visibility = collapsed || !_settings.ShowCharts ? Visibility.Collapsed : Visibility.Visible;
            _panel.Element.Visibility = _settingsOpen && !collapsed ? Visibility.Visible : Visibility.Collapsed;
            _gear.Active = _settingsOpen && !collapsed;
            _range.Select(Math.Max(0, Array.IndexOf(Settings.Ranges, _settings.RangeMinutes)));
            if (_settingsOpen) _panel.SyncFooter();

            _cards.Children.Clear();
            foreach (var id in _settings.CardOrder)
            {
                FrameworkElement card;
                if (_settings.IsCardVisible(id) && _cardViews.TryGetValue(id, out card)) _cards.Children.Add(card);
            }
            _cards.Visibility = _cards.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _emptyHint.Visibility = _cards.Children.Count == 0 && !_settings.ShowCharts && !_settingsOpen ? Visibility.Visible : Visibility.Collapsed;

            _charts.Children.Clear();
            _charts.Visibility = _settings.ShowCharts ? Visibility.Visible : Visibility.Collapsed;
            _charts.Margin = new Thickness(0, _cards.Children.Count > 0 ? Gap : 0, 0, 0);
            if (_settings.Layout == ChartLayout.Single) _charts.Children.Add(_singleCard.Element);
            else { _charts.Children.Add(_tempCard.Element); _charts.Children.Add(_loadCard.Element); }
            _charts.ForceSingleColumn = _settings.Layout == ChartLayout.Stacked;
            _charts.InvalidateMeasure();

            int points = _settings.RangeMinutes * 60;
            foreach (var c in new[] { _tempCard, _loadCard, _singleCard }) c.Apply(points);
            _singleCard.SetMode(_settings.SingleMode);
            SafeRefresh();
        }

        static double Gap { get { return _compact ? 10 : 16; } }

        /// <summary>Creates every card and chart for the current settings.</summary>
        void Build()
        {
            _compact = _settings.Compact;
            _refreshers.Clear();
            _cardViews.Clear();
            _cards = new ColumnsPanel { Gap = Gap, MinColumnWidth = _compact ? 240 : 280 };
            _charts = new ColumnsPanel { Gap = Gap, MinColumnWidth = 440 };
            _header.Margin = new Thickness(2, 0, 0, _compact ? 6 : 10);
            _panel.Element.Margin = new Thickness(0, 0, 0, Gap);

            _cardViews["cpu"] = CpuCard();
            _cardViews["gpu"] = GpuCard();
            _cardViews["ram"] = RamCard();
            _cardViews["fans"] = FanCard();
            foreach (var c in _settings.Custom) _cardViews[c.CardId] = CustomCard(c);
            _emptyHint = EmptyHint();

            var temp = SeriesFor(1);
            var load = SeriesFor(2);
            _tempCard = new ChartCard("Temperature", Units.Temp, 0, _settings, false, OnHover);
            _loadCard = new ChartCard("Load", "%", 1, _settings, false, OnHover);
            _singleCard = new ChartCard("History", null, _settings.SingleMode, _settings, true, OnHover);
            foreach (var c in new[] { _tempCard, _loadCard, _singleCard }) c.Chart.SetSeries(temp, load);

            _body.Children.Clear();
            _body.Children.Add(_panel.Element);
            _body.Children.Add(_emptyHint);
            _body.Children.Add(_cards);
            _body.Children.Add(_charts);
        }

        /// <summary>Lines of the temperature (1) or load (2) chart: the built-in ones, then the user's sensors.</summary>
        SeriesDef[] SeriesFor(int chart)
        {
            Func<string, Brush> color = key => Palette.ForSeries(_settings, key);
            var list = new List<SeriesDef>();
            if (chart == 1)
            {
                var cpu = new SeriesDef("CPU", "cpu.temp", color("cpu.temp"), "°C") { LimitLabel = "CPU limit" };
                cpu.Limit = () => _settings.CpuLimit;
                var ram = new SeriesDef("RAM", "ram.temp", color("ram.temp"), "°C") { LimitLabel = "RAM warning" };
                ram.Limit = () => _settings.EffectiveRamWarning(_sampler.Get("ram.limit"));
                list.Add(cpu);
                list.Add(new SeriesDef("GPU", "gpu.temp", color("gpu.temp"), "°C"));
                list.Add(new SeriesDef("GPU hotspot", "gpu.hot", color("gpu.hot"), "°C", "Hotspot"));
                list.Add(new SeriesDef("VRAM", "gpu.vram", color("gpu.vram"), "°C"));
                list.Add(ram);
            }
            else
            {
                list.Add(new SeriesDef("CPU", "cpu.load", color("cpu.load"), "%"));
                list.Add(new SeriesDef("GPU", "gpu.load", color("gpu.load"), "%"));
                list.Add(new SeriesDef("RAM", "ram.load", color("ram.load"), "%"));
            }
            foreach (var c in _settings.Custom.Where(c => c.Chart == chart))
            {
                var item = c;
                var def = new SeriesDef(c.Name, c.Key, color(c.Key), c.Unit) { LimitLabel = c.Name + " limit" };
                def.Limit = () => item.Limit;
                list.Add(def);
            }
            return list.ToArray();
        }

        FrameworkElement EmptyHint()
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            var t = Small("Every card and chart is hidden.", Palette.TextDim); t.Margin = new Thickness(0, 0, 12, 0);
            p.Children.Add(t);
            p.Children.Add(new TextButton("Choose what to show", () => { if (!_settingsOpen) ToggleSettings(); _panel.Tab = 1; }));
            return Card(p);
        }

        void OnHover(Chart source, double fraction)
        {
            foreach (var c in new[] { _tempCard, _loadCard, _singleCard })
                if (!ReferenceEquals(c.Chart, source)) c.Chart.Sync(fraction);
        }

        // ---------- cards ----------

        FrameworkElement CpuCard()
        {
            var detail = Small("", Palette.TextMuted); var big = Big(); var status = Small(""); var loadText = Value(); var power = Small("");
            var bar = new Bar(Palette.ForSeries(_settings, "cpu.load"));
            var s = new StackPanel();
            s.Children.Add(Header("CPU", detail, Palette.ForSeries(_settings, "cpu.temp")));
            s.Children.Add(big);
            s.Children.Add(status);
            s.Children.Add(LoadRow(bar, loadText, "Load", "Total CPU load"));
            power.Margin = new Thickness(0, _compact ? 4 : 8, 0, 0);
            s.Children.Add(power);
            _refreshers.Add(() =>
            {
                float t = _sampler.Get("cpu.temp"), l = _sampler.Get("cpu.load"), p = _sampler.Get("cpu.power");
                detail.Text = _sampler.SensorName("cpu.temp") ?? "";
                SetBig(big, t, "°C");
                Headroom(status, t, _settings.CpuLimit, "limit", 5, "No CPU temperature sensor found", "°C");
                bar.Value = l; loadText.Text = Units.Format(l, "%");
                power.Text = float.IsNaN(p) ? "" : "Package power " + Units.Format(p, "W");
                power.Visibility = float.IsNaN(p) || !_settings.ShowCpuPower ? Visibility.Collapsed : Visibility.Visible;
            });
            return Card(s);
        }

        FrameworkElement GpuCard()
        {
            var detail = Small("", Palette.TextMuted); var big = Big(); var hot = Small(""); var vram = Small(""); var status = Small(""); var loadText = Value();
            var bar = new Bar(Palette.ForSeries(_settings, "gpu.load"));
            var s = new StackPanel();
            s.Children.Add(Header("GPU", detail, Palette.ForSeries(_settings, "gpu.temp")));
            s.Children.Add(big);
            var temps = new WrapPanel();
            var hotChip = Chip(Palette.ForSeries(_settings, "gpu.hot"), hot); var vramChip = Chip(Palette.ForSeries(_settings, "gpu.vram"), vram);
            temps.Children.Add(hotChip); temps.Children.Add(vramChip); temps.Children.Add(status);
            s.Children.Add(temps);
            var loadRow = LoadRow(bar, loadText, "Load", "Busiest GPU engine (3D, compute, video…), the same number Task Manager shows");
            s.Children.Add(loadRow);
            _refreshers.Add(() =>
            {
                float t = _sampler.Get("gpu.temp"), h = _sampler.Get("gpu.hot"), v = _sampler.Get("gpu.vram"), l = _sampler.Get("gpu.load");
                detail.Text = _sampler.SensorName("gpu.temp") ?? "";
                SetBig(big, t, "°C");
                hot.Text = "Hotspot " + Units.Format(h, "°C");
                vram.Text = "VRAM " + Units.Format(v, "°C");
                hotChip.Visibility = float.IsNaN(h) || !_settings.ShowGpuDetails ? Visibility.Collapsed : Visibility.Visible;
                vramChip.Visibility = float.IsNaN(v) || !_settings.ShowGpuDetails ? Visibility.Collapsed : Visibility.Visible;
                bool none = _sampler.Discovered && float.IsNaN(t) && float.IsNaN(h) && float.IsNaN(l);
                Status(status, none ? "No GPU sensors found" : float.IsNaN(t) && _sampler.Discovered ? "No GPU temperature sensor found" : "", Palette.TextMuted);
                status.Margin = new Thickness(0, none ? 6 : 0, 0, 0);
                status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                big.Visibility = loadRow.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
                bar.Value = l; loadText.Text = Units.Format(l, "%");
            });
            return Card(s);
        }

        FrameworkElement RamCard()
        {
            var detail = Small("", Palette.TextMuted); var big = Big(); var status = Small(""); var loadText = Value(); var gb = Small(""); var modules = Small("", Palette.TextMuted);
            var bar = new Bar(Palette.ForSeries(_settings, "ram.load"));
            var s = new StackPanel();
            s.Children.Add(Header("RAM", detail, Palette.ForSeries(_settings, "ram.temp")));
            s.Children.Add(big);
            s.Children.Add(status);
            s.Children.Add(LoadRow(bar, loadText, "Used", "Physical memory in use"));
            gb.Margin = new Thickness(0, _compact ? 4 : 8, 0, 0);
            s.Children.Add(gb);
            modules.TextTrimming = TextTrimming.CharacterEllipsis; modules.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(modules);
            _refreshers.Add(() =>
            {
                // module temperature when there is one (DDR5 or a chosen sensor), otherwise the amount in use
                float t = _sampler.Get("ram.temp"), l = _sampler.Get("ram.load"), u = _sampler.Get("ram.used"), tot = _sampler.Get("ram.total");
                var names = _sampler.DimmNames;
                bool chosen = _settings.Sensors.ContainsKey("ram.temp");
                if (!float.IsNaN(t))
                {
                    detail.Text = chosen ? (_sampler.SensorName("ram.temp") ?? "") : names.Count > 1 ? "hottest module" : "module";
                    SetBig(big, t, "°C");
                    Headroom(status, t, _settings.EffectiveRamWarning(_sampler.Get("ram.limit")), "warning", 3, null, "°C");
                    gb.Text = float.IsNaN(u) ? "" : string.Format(Inv, "{0:0.0} of {1:0} GB in use", u, tot);
                    gb.Foreground = Palette.TextDim;
                    gb.Visibility = _settings.ShowRamUsage && gb.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    detail.Text = "in use";
                    SetBig(big, u, "GB", "0.0");
                    Status(status, float.IsNaN(tot) ? "" : string.Format(Inv, "of {0:0} GB", tot), Palette.TextDim);
                    gb.Text = _sampler.Discovered ? "The modules report no temperature" : "";
                    gb.Foreground = Palette.TextMuted;
                    gb.Visibility = gb.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                bar.Value = l; loadText.Text = Units.Format(l, "%");
                var parts = new List<string>();
                if (names.Count > 1 && !chosen)
                    for (int d = 0; d < names.Count; d++) { float v = _sampler.Get("ram.dimm" + d); if (!float.IsNaN(v)) parts.Add(names[d] + " " + Units.Format(v, "°C")); }
                modules.Text = string.Join(" · ", parts);
                modules.Visibility = parts.Count > 0 && _settings.ShowRamModules ? Visibility.Visible : Visibility.Collapsed;
            });
            return Card(s);
        }

        FrameworkElement FanCard()
        {
            var grid = new Grid();
            var empty = Small("", Palette.TextMuted);
            var rows = new List<Tuple<FanInfo, Bar, TextBlock, TextBlock>>();
            string signature = null;
            var s = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, _compact ? 4 : 8) };
            var unit = Small("duty · speed", Palette.TextMuted); DockPanel.SetDock(unit, Dock.Right);
            head.Children.Add(unit); head.Children.Add(Label("Fans"));
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
                        : fans.Count > 0 ? "All fans are hidden. Choose them in the settings (Fans tab)."
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

        /// <summary>A card for a sensor the user added: value, optional headroom to its limit, a bar for percentages.</summary>
        FrameworkElement CustomCard(CustomItem item)
        {
            var c = item;
            var detail = Small("", Palette.TextMuted); var big = Big(); var status = Small(""); var barText = Value();
            var bar = new Bar(Palette.ForSeries(_settings, c.Key));
            var s = new StackPanel();
            s.Children.Add(Header(c.Name, detail, Palette.ForSeries(_settings, c.Key)));
            s.Children.Add(big);
            s.Children.Add(status);
            var barRow = LoadRow(bar, barText, "", c.Name);
            barRow.Visibility = c.Unit == "%" ? Visibility.Visible : Visibility.Collapsed;
            s.Children.Add(barRow);
            _refreshers.Add(() =>
            {
                float v = _sampler.Get(c.Key);
                var info = _sampler.Catalog.FirstOrDefault(x => x.Id == c.SensorId);
                detail.Text = info != null && info.Name != c.Name ? info.Name : "";
                big.Inlines.Clear();
                big.Inlines.Add(new Run(Units.Number(v, c.Unit)));
                if (c.Unit.Length > 0) big.Inlines.Add(new Run(" " + Units.DisplayUnit(c.Unit)) { FontSize = _compact ? 12 : 14, Foreground = Palette.TextDim });
                if (float.IsNaN(v)) Status(status, _sampler.Discovered && info == null && _sampler.Catalog.Count > 0 ? "Sensor not found in Fan Control" : "", Palette.TextMuted);
                else if (!double.IsNaN(c.Limit)) Headroom(status, v, c.Limit, "limit", c.Unit == "°C" ? 5 : Math.Abs(c.Limit) * 0.05, null, c.Unit);
                else Status(status, "", Palette.TextDim);
                status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                bar.Value = v; barText.Text = Units.Format(v, "%");
            });
            return Card(s);
        }

        // ---------- refresh ----------

        void SafeRefresh()
        {
            try { Refresh(); } catch { /* never take Fan Control down */ }
        }

        void Refresh()
        {
            foreach (var r in _refreshers) { try { r(); } catch { } }
            UpdateSubtitle(_sampler.Get("cpu.temp"), _sampler.Get("gpu.temp"), _sampler.Get("ram.temp"));
            foreach (var c in new[] { _tempCard, _loadCard, _singleCard }) c.SyncAvailability(_sampler);
            if (_settingsOpen) _panel.SyncRam();
            if (_settings.ShowCharts && !_settings.Collapsed)
                foreach (var c in new[] { _tempCard, _loadCard, _singleCard }) c.Chart.InvalidateVisual();
        }

        void UpdateSubtitle(float cpuT, float gpuT, float ramT)
        {
            double stale = _sampler.LastSampleUtc == DateTime.MinValue ? double.PositiveInfinity : (DateTime.UtcNow - _sampler.LastSampleUtc).TotalSeconds;
            if (!_sampler.Discovered || stale > 10)
            {
                _subtitle.Text = !_sampler.Discovered ? "Connecting to Fan Control…" : string.Format(Inv, "No new values from Fan Control for {0:0} s", stale);
                _subtitle.Foreground = _sampler.Discovered ? Palette.Warn : Palette.TextMuted;
                return;
            }
            _subtitle.Foreground = Palette.TextMuted;
            if (_settings.Collapsed)
            {
                var bits = new List<string>();
                if (!float.IsNaN(cpuT)) bits.Add("CPU " + Units.Format(cpuT, "°C"));
                if (!float.IsNaN(gpuT)) bits.Add("GPU " + Units.Format(gpuT, "°C"));
                if (!float.IsNaN(ramT)) bits.Add("RAM " + Units.Format(ramT, "°C"));
                foreach (var c in _settings.Custom.Where(c => c.ShowCard))
                {
                    float v = _sampler.Get(c.Key);
                    if (!float.IsNaN(v)) bits.Add(c.Name + " " + Units.Format(v, c.Unit));
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

        /// <summary>"24 °C below the 95 °C limit", amber within <paramref name="warnWithin"/> (in °C or the unit), red at or over it.</summary>
        static void Headroom(TextBlock t, float value, double limit, string word, double warnWithin, string missing, string unit)
        {
            if (float.IsNaN(value)) { Status(t, missing ?? "", Palette.TextMuted); return; }
            bool temp = unit == "°C";
            double d = temp ? Math.Round(limit) - Math.Round(value) : limit - value;
            Func<double, string> amount = x => temp ? Math.Round(Units.DeltaToDisplay(x)).ToString("0", Inv) + " " + Units.Temp : Units.Format((float)x, unit);
            string lim = temp ? Units.Format((float)limit, "°C") : Units.Format((float)limit, unit);
            if (d > warnWithin) Status(t, amount(d) + " below the " + lim + " " + word, Palette.TextDim);
            else if (d > 0) Status(t, amount(d) + " below the " + lim + " " + word, Palette.Warn);
            else if (d == 0) Status(t, "At the " + lim + " " + word, Palette.Critical);
            else Status(t, amount(-d) + " above the " + lim + " " + word, Palette.Critical);
        }

        static void Status(TextBlock t, string text, Brush b) { t.Text = text; t.Foreground = b; }

        static void SetBig(TextBlock t, float v, string unit, string format = "0")
        {
            t.Inlines.Clear();
            bool temp = unit == "°C";
            t.Inlines.Add(new Run(float.IsNaN(v) ? "–" : (temp ? Units.ToDisplay(v) : v).ToString(format, Inv)));
            t.Inlines.Add(new Run(" " + (temp ? Units.Temp : unit)) { FontSize = _compact ? 12 : 14, Foreground = Palette.TextDim });
        }

        // ---------- small building blocks ----------

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

        static FrameworkElement Chip(Brush swatch, TextBlock text)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            p.Children.Add(Swatch(swatch)); p.Children.Add(text);
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
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(label.Length > 0 ? 42 : 0) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            var l = Small(label); Grid.SetColumn(l, 0); g.Children.Add(l);
            bar.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bar, 1); g.Children.Add(bar);
            Grid.SetColumn(text, 2); g.Children.Add(text);
            return g;
        }
    }

    /// <summary>A history chart in its own card: title, unit, clickable legend, optional temperature/load toggle.</summary>
    sealed class ChartCard
    {
        public readonly Chart Chart;
        public readonly FrameworkElement Element;
        readonly Settings _settings;
        readonly WrapPanel _legend = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        readonly Segmented _toggle;
        readonly TextBlock _unit;
        readonly HashSet<string> _seen = new HashSet<string>();
        bool _discovered;
        string _legendSignature;

        public ChartCard(string title, string unit, int mode, Settings settings, bool withToggle, Action<Chart, double> hover)
        {
            _settings = settings;
            Chart = new Chart(settings) { Mode = mode, Margin = new Thickness(0, 10, 0, 0) };
            Chart.HoverChanged = hover;
            Chart.IsAvailable = key => !_discovered || _seen.Contains(key);
            AutomationProperties.SetName(Chart, title + " history chart");
            var head = new DockPanel { LastChildFill = true };
            if (withToggle)
            {
                _toggle = new Segmented(new[] { "Temperature", "Load" }, i => { _settings.SingleMode = i; _settings.Save(); }, "Chart shows");
                _toggle.VerticalAlignment = VerticalAlignment.Top; _toggle.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(_toggle, Dock.Right);
                head.Children.Add(_toggle);
            }
            var titles = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
            titles.Children.Add(DashboardView.Label(title));
            _unit = DashboardView.Small(unit ?? "", Palette.TextMuted); _unit.Margin = new Thickness(6, 1, 0, 0);
            titles.Children.Add(_unit);
            DockPanel.SetDock(titles, Dock.Left);
            head.Children.Add(titles);
            _legend.Margin = new Thickness(12, 0, 0, 0);
            head.Children.Add(_legend);
            var s = new StackPanel();
            s.Children.Add(head);
            s.Children.Add(Chart);
            Element = DashboardView.Card(s);
        }

        public void Apply(int points)
        {
            Chart.Points = points;
            Chart.Height = _settings.ChartHeight;
            UpdateLegend();
            Chart.InvalidateVisual();
        }

        public void SetMode(int mode)
        {
            if (_toggle == null) return;
            Chart.Mode = mode; _toggle.Select(mode); _unit.Text = mode == 0 ? Units.Temp : "%";
            UpdateLegend(); Chart.InvalidateVisual();
        }

        string Signature() { return _discovered + ":" + Chart.Mode + ":" + string.Join(",", Chart.AllSeries.Where(d => _seen.Contains(d.Key)).Select(d => d.Key)); }

        /// <summary>Lists only series that have (had) data, once Fan Control's sensors are known.</summary>
        public void SyncAvailability(Sampler sampler)
        {
            _discovered = sampler.Discovered;
            foreach (var d in Chart.AllSeries)
                if (!float.IsNaN(sampler.Get(d.Key))) _seen.Add(d.Key);
            if (Signature() != _legendSignature) UpdateLegend();
        }

        bool Available(SeriesDef d) { return !_discovered || _seen.Contains(d.Key); }

        void UpdateLegend()
        {
            _legendSignature = Signature();
            _legend.Children.Clear();
            foreach (var s in Chart.AllSeries)
            {
                var def = s;
                if (!Available(def)) continue;
                bool hidden = _settings.IsHidden(Chart.Mode, def.Key);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(DashboardView.Swatch(def.Brush, hidden));
                var label = DashboardView.Small(def.Name, hidden ? Palette.TextMuted : Palette.TextDim);
                if (hidden) label.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(label);
                var item = new Pressable((hidden ? "Show " : "Hide ") + def.Name, () => { _settings.ToggleHidden(Chart.Mode, def.Key); _settings.Save(); }, 5)
                {
                    Child = row, Padding = new Thickness(6, 2, 6, 3), Margin = new Thickness(0, 1, 2, 1),
                    ToolTip = (hidden ? "Show " : "Hide ") + def.Name,
                };
                _legend.Children.Add(item);
            }
        }
    }
}
