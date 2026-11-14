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

namespace FanControl.Dashboard
{
    /// <summary>
    /// The dashboard's settings card: General, Cards, Sensors, Custom, Fans and Look tabs. Every control saves at once.
    /// A tab's controls are built when the tab is shown, and rebuilt only after changes that alter the list itself
    /// (add, remove, move, reset), so typing and keyboard focus are never interrupted by a refresh.
    /// </summary>
    sealed class SettingsPanel
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string GlyphClose = "", GlyphUp = "", GlyphDown = "", GlyphDelete = "", GlyphRefresh = "";
        public static readonly string[] TabNames = { "General", "Cards", "Sensors", "Custom", "Fans", "Look" };

        readonly Settings _settings;
        readonly Sampler _sampler;
        readonly Action _close;
        readonly TabStrip _tabs;
        readonly ContentControl _page = new ContentControl { Focusable = false };
        readonly TextButton _reset;
        public readonly FrameworkElement Element;
        bool _building;

        public SettingsPanel(Settings settings, Sampler sampler, Action close)
        {
            _settings = settings; _sampler = sampler; _close = close;

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var closeButton = new IconButton(GlyphClose, "Close settings", close) { Width = 26, Height = 26, Margin = new Thickness(0, -4, -6, 0) };
            DockPanel.SetDock(closeButton, Dock.Right);
            head.Children.Add(closeButton);
            head.Children.Add(DashboardView.Label("Dashboard settings"));

            _tabs = new TabStrip(TabNames, i => ShowTab()) { Margin = new Thickness(-10, 4, 0, 0) };

            var foot = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            _reset = new TextButton("Reset to defaults", () => { _settings.Reset(); _settings.Save(); ShowTab(); });
            _reset.ToolTip = "Restore every option. Your custom sensors and fan names are kept.";
            DockPanel.SetDock(_reset, Dock.Right);
            foot.Children.Add(_reset);
            var hint = Hint("Changes are saved automatically. Click a legend entry in a chart to hide or show a line.");
            hint.Margin = new Thickness(0, 0, 12, 0);
            foot.Children.Add(hint);

            var s = new StackPanel();
            s.Children.Add(head);
            s.Children.Add(_tabs);
            s.Children.Add(new Rectangle { Height = 1, Fill = Palette.Grid, SnapsToDevicePixels = true });
            _page.Margin = new Thickness(0, 12, 0, 0);
            s.Children.Add(_page);
            s.Children.Add(new Rectangle { Height = 1, Fill = Palette.Grid, Margin = new Thickness(0, 12, 0, 0), SnapsToDevicePixels = true });
            s.Children.Add(foot);
            Element = DashboardView.Card(s);
            Element.KeyDown += (o, e) => { if (e.Key == Key.Escape && !(e.OriginalSource is TextBox) && !(e.OriginalSource is ComboBox)) { _close(); e.Handled = true; } };
            AutomationProperties.SetName(Element, "Dashboard settings");
            _tabs.Select(0, false);
        }

        public int Tab { get { return _tabs.Selected; } set { _tabs.Select(Math.Max(0, Math.Min(TabNames.Length - 1, value)), false); ShowTab(); } }

        /// <summary>Rebuilds the visible tab (e.g. after a reset or when the sensor list arrived).</summary>
        public void ShowTab()
        {
            _building = true;
            try
            {
                switch (_tabs.Selected)
                {
                    case 1: _page.Content = CardsTab(); break;
                    case 2: _sampler.RequestDiscovery(); _page.Content = SensorsTab(); break;
                    case 3: _sampler.RequestDiscovery(); _page.Content = CustomTab(); break;
                    case 4: _page.Content = FansTab(); break;
                    case 5: _page.Content = LookTab(); break;
                    default: _page.Content = GeneralTab(); break;
                }
            }
            finally { _building = false; }
            SyncFooter();
        }

        public void SyncFooter() { _reset.IsEnabled = !_settings.IsDefault; }

        void Save() { if (!_building) { _settings.Save(); SyncFooter(); } }

        /// <summary>Saves, then rebuilds the tab (for changes to the list shown on it).</summary>
        void SaveAndRefresh() { _settings.Save(); ShowTab(); }

        // ---------------------------------------------------------------- General

        TextBox _ramWarnBox; TextBlock _ramNote; CheckBox _ramAuto;

        FrameworkElement GeneralTab()
        {
            var g = new Form();
            g.Add("History", Check("Show history charts", _settings.ShowCharts, v => _settings.ShowCharts = v));
            g.Add("Layout", Left(new Segmented(new[] { "Side by side", "Stacked", "One chart" }, i => { _settings.Layout = (ChartLayout)i; Save(); }, "Chart layout"), (int)_settings.Layout));
            int h = Array.IndexOf(Settings.Heights, _settings.ChartHeight);
            g.Add("Chart height", Left(new Segmented(new[] { "Small", "Medium", "Large" }, i => { _settings.ChartHeight = Settings.Heights[i]; Save(); }, "Chart height"), h < 0 ? 1 : h));

            var limits = new StackPanel();
            limits.Children.Add(Check("Show limit lines in the charts", _settings.ShowLimits, v => _settings.ShowLimits = v));
            var cpu = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            cpu.Children.Add(Inline("CPU limit", TempBox("CPU limit", _settings.CpuLimit, Settings.CpuLimitMin, Settings.CpuLimitMax, v => _settings.CpuLimit = v),
                "Your CPU's maximum temperature (Tjmax): about 95 °C for AMD Ryzen 7000/9000, 89 °C for Ryzen X3D, 100 °C for most Intel desktop CPUs."));
            limits.Children.Add(cpu);
            var ram = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _ramWarnBox = TempBox("RAM warning", _settings.RamWarning, Settings.RamWarningMin, Settings.RamWarningMax, v => _settings.RamWarning = v);
            ram.Children.Add(Inline("RAM warning", _ramWarnBox, "Memory module temperature to warn at."));
            _ramAuto = Check("Use the modules' own limit", _settings.RamWarningAuto, v => { _settings.RamWarningAuto = v; SyncRam(); });
            _ramAuto.ToolTip = "DDR5 modules report a \"high\" temperature threshold (usually 55 °C). Use it when available.";
            _ramAuto.Margin = new Thickness(0, 0, 10, 0);
            ram.Children.Add(_ramAuto);
            _ramNote = DashboardView.Small("", Palette.TextMuted);
            ram.Children.Add(_ramNote);
            limits.Children.Add(ram);
            g.Add("Limits", limits);
            SyncRam();
            return g.Element;
        }

        /// <summary>Called every second while the settings are open: the module limit can arrive after the tab was built.</summary>
        public void SyncRam()
        {
            if (_ramWarnBox == null || _tabs.Selected != 0) return;
            float moduleLimit = _sampler.Get("ram.limit");
            bool known = !float.IsNaN(moduleLimit), auto = _settings.RamWarningAuto && known;
            _ramWarnBox.IsEnabled = !auto;
            _ramNote.Text = !_settings.RamWarningAuto ? "" : known ? "(" + Units.Format(moduleLimit, "°C") + " reported)" : "(not reported, using the value)";
            if (!_ramWarnBox.IsKeyboardFocused) _ramWarnBox.Text = Units.ToDisplay(auto ? moduleLimit : _settings.RamWarning).ToString("0", Inv);
        }

        // ---------------------------------------------------------------- Cards

        static readonly Dictionary<string, string> CardNames = new Dictionary<string, string> { { "cpu", "CPU" }, { "gpu", "GPU" }, { "ram", "RAM" }, { "fans", "Fans" } };

        FrameworkElement CardsTab()
        {
            var list = new StackPanel();
            list.Children.Add(Hint("Cards appear in this order, left to right and then down. Untick a card to hide it."));
            var order = _settings.CardOrder;
            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i];
                var custom = _settings.FindCustom(id);
                string name = custom != null ? custom.Name : CardNames[id];
                var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                var moves = Moves(name, i > 0 ? () => { _settings.MoveCard(id, -1); SaveAndRefresh(); } : (Action)null,
                                        i < order.Count - 1 ? () => { _settings.MoveCard(id, +1); SaveAndRefresh(); } : (Action)null);
                DockPanel.SetDock(moves, Dock.Right);
                row.Children.Add(moves);
                var main = new StackPanel();
                var check = Check(name, _settings.IsCardVisible(id), v => _settings.SetCardVisible(id, v));
                check.FontWeight = FontWeights.SemiBold;
                main.Children.Add(check);
                var details = new WrapPanel { Margin = new Thickness(26, 2, 0, 0) };
                switch (id)
                {
                    case "cpu": details.Children.Add(SmallCheck("Package power", _settings.ShowCpuPower, v => _settings.ShowCpuPower = v)); break;
                    case "gpu": details.Children.Add(SmallCheck("Hotspot and VRAM", _settings.ShowGpuDetails, v => _settings.ShowGpuDetails = v)); break;
                    case "ram":
                        details.Children.Add(SmallCheck("Amount in use", _settings.ShowRamUsage, v => _settings.ShowRamUsage = v));
                        details.Children.Add(SmallCheck("Per-module temperatures", _settings.ShowRamModules, v => _settings.ShowRamModules = v));
                        break;
                    case "fans": details.Children.Add(Link("Choose fans, names and order", () => Tab = 4)); break;
                    default: details.Children.Add(Link("Edit sensor, chart and limit", () => Tab = 3)); break;
                }
                main.Children.Add(details);
                row.Children.Add(main);
                list.Children.Add(row);
            }
            var add = Link("+ Add a card for any sensor", () => Tab = 3);
            add.Margin = new Thickness(0, 12, 0, 0);
            list.Children.Add(add);
            return list;
        }

        // ---------------------------------------------------------------- Sensors

        FrameworkElement SensorsTab()
        {
            var catalog = _sampler.Catalog;
            var p = new StackPanel();
            var top = new DockPanel();
            var refresh = new TextButton("Refresh sensor list", () => { _sampler.RequestDiscovery(); RefreshSoon(); });
            DockPanel.SetDock(refresh, Dock.Right);
            top.Children.Add(refresh);
            top.Children.Add(Hint("The dashboard picks its sensors automatically. Choose another one here, e.g. a different CPU temperature, a motherboard sensor or one of Fan Control's mix or custom sensors."));
            p.Children.Add(top);
            if (catalog.Count == 0) { p.Children.Add(Empty("Waiting for Fan Control's sensor list…")); RefreshSoon(); return p; }

            var g = new Form { LabelWidth = 150 };
            foreach (var slot in Settings.SensorSlots)
            {
                string key = slot[0], type = slot[2];
                string auto = _sampler.AutomaticSensorId(key);
                var autoSensor = catalog.FirstOrDefault(c => c.Id == auto);
                string autoText = key == "gpu.load" && auto == null ? "Automatic (Windows GPU counters, like Task Manager)"
                                : key == "ram.temp" && auto == null ? "Automatic (hottest memory module)"
                                : "Automatic" + (autoSensor != null ? " (" + SensorLabel(autoSensor) + ")" : " (none found)");
                var items = new List<Pick> { new Pick(autoText, null) };
                items.AddRange(Sorted(catalog.Where(c => c.Type == type)).Select(c => new Pick(SensorLabel(c), c.Id)));
                string current; _settings.Sensors.TryGetValue(key, out current);
                if (current != null && items.All(i => i.Id != current)) items.Add(new Pick("Missing sensor: " + current, current));
                var combo = Combo(slot[1], items, current, id =>
                {
                    if (id == null) _settings.Sensors.Remove(key); else _settings.Sensors[key] = id;
                    Save();
                });
                g.Add(slot[1], Fit(combo, 400));
            }
            p.Children.Add(g.Element);
            return p;
        }

        // ---------------------------------------------------------------- Custom

        FrameworkElement CustomTab()
        {
            var catalog = _sampler.Catalog;
            var p = new StackPanel();
            p.Children.Add(Hint("Add any sensor Fan Control knows as its own card and/or as a line in the temperature or load chart: a water or case temperature, a mix sensor, a pump speed, power, voltage…"));


            var pickable = Sorted(catalog.Where(c => c.Type != "Unsupported")).Select(c => new Pick(SensorLabel(c) + "  (" + TypeName(c.Type) + ")", c.Id)).ToList();
            pickable.Insert(0, new Pick(catalog.Count == 0 ? "Waiting for Fan Control's sensor list…" : "Choose a sensor to add…", null));
            ComboBox chooser = null;
            var add = new TextButton("Add", () =>
            {
                var pick = chooser.SelectedItem as Pick;
                if (pick == null || pick.Id == null) { chooser.Focus(); chooser.IsDropDownOpen = true; return; }
                var s = catalog.First(c => c.Id == pick.Id);
                var item = _settings.AddCustom(s.Id, s.Name, Units.ForType(s.Type));
                if (item == null) return;
                SaveAndRefresh();
            });
            add.Margin = new Thickness(8, 0, 0, 0);
            add.IsEnabled = _settings.Custom.Count < Settings.MaxCustom;
            chooser = Combo("Sensor to add", pickable, null, id => { });
            var addRow = Fit(chooser, 460, add);
            addRow.Margin = new Thickness(0, 10, 0, 4);
            p.Children.Add(addRow);
            if (catalog.Count == 0) RefreshSoon();

            if (_settings.Custom.Count == 0) p.Children.Add(Empty("No custom sensors yet."));
            foreach (var c in _settings.Custom.ToList()) p.Children.Add(CustomEditor(c, catalog));
            return p;
        }

        FrameworkElement CustomEditor(CustomItem c, List<SensorInfo> catalog)
        {
            var box = new Border { BorderBrush = Palette.CardEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 8, 10), Margin = new Thickness(0, 10, 0, 0) };
            var s = new StackPanel();

            var top = new DockPanel();
            var del = new RowButton(GlyphDelete, "Remove " + c.Name, () => { _settings.RemoveCustom(c.Id); SaveAndRefresh(); });
            DockPanel.SetDock(del, Dock.Right);
            top.Children.Add(del);
            var name = TextField("Name", c.Name, 220, v => { c.Name = string.IsNullOrWhiteSpace(v) ? c.Name : v.Trim(); Save(); });
            name.HorizontalAlignment = HorizontalAlignment.Left;
            top.Children.Add(name);
            s.Children.Add(top);

            var g = new Form { LabelWidth = 80, Margin = new Thickness(0, 6, 0, 0) };
            var items = Sorted(catalog.Where(x => x.Type != "Unsupported")).Select(x => new Pick(SensorLabel(x) + "  (" + TypeName(x.Type) + ")", x.Id)).ToList();
            if (items.All(i => i.Id != c.SensorId)) items.Insert(0, new Pick(catalog.Count == 0 ? "Loading…" : "Missing sensor: " + c.SensorId, c.SensorId));
            g.Add("Sensor", Fit(Combo("Sensor for " + c.Name, items, c.SensorId, id =>
            {
                var sensor = catalog.FirstOrDefault(x => x.Id == id);
                if (sensor == null) return;
                c.SensorId = id;
                string unit = Units.ForType(sensor.Type);
                if (unit != c.Unit) { c.Unit = unit; c.Limit = double.NaN; c.Chart = unit == "°C" ? 1 : unit == "%" ? 2 : 0; SaveAndRefresh(); }
                else Save();
            }), 400));

            var show = new WrapPanel();
            show.Children.Add(Check("Card", c.ShowCard, v => c.ShowCard = v));
            int[] chartOf = { 0, c.Unit == "°C" ? 1 : c.Unit == "%" ? 2 : -1 };
            if (chartOf[1] > 0)
                show.Children.Add(Check(chartOf[1] == 1 ? "Line in the temperature chart" : "Line in the load chart", c.Chart != 0, v => c.Chart = v ? chartOf[1] : 0));
            else
                show.Children.Add(DashboardView.Small("Only °C and % sensors can be drawn in the charts.", Palette.TextMuted));
            g.Add("Show as", show);

            var limit = new WrapPanel();
            bool temp = c.Unit == "°C";
            var lbox = LimitBox(c, temp);
            limit.Children.Add(lbox);
            limit.Children.Add(DashboardView.Small(" " + Units.DisplayUnit(c.Unit) + "   (empty = no limit)", Palette.TextMuted));
            g.Add("Limit", limit);

            g.Add("Colour", new ColorPicker(c.Name, c.Color, Palette.DefaultColor(c.Key, _settings.Custom.IndexOf(c)), v => { c.Color = v; Save(); }));
            s.Children.Add(g.Element);
            box.Child = s;
            return box;
        }

        TextBox LimitBox(CustomItem c, bool temp)
        {
            var t = Field("Limit for " + c.Name, 60);
            Func<string> current = () => double.IsNaN(c.Limit) ? "" : (temp ? Units.ToDisplay(c.Limit) : c.Limit).ToString("0.##", Inv);
            t.Text = current();
            t.ToolTip = "The card shows the headroom to this value and the chart draws a line. Leave empty for none.";
            Action commit = () =>
            {
                double v;
                if (string.IsNullOrWhiteSpace(t.Text)) c.Limit = double.NaN;
                else if (double.TryParse(t.Text.Replace(',', '.'), NumberStyles.Float, Inv, out v) && Math.Abs(v) < 1e6) c.Limit = temp ? Units.FromDisplay(v) : v;
                t.Text = current();
                Save();
            };
            Commits(t, commit, () => t.Text = current());
            return t;
        }

        // ---------------------------------------------------------------- Fans

        FrameworkElement FansTab()
        {
            var fans = _sampler.Fans;
            var p = new StackPanel();
            p.Children.Add(Hint("Names here only change the dashboard, not Fan Control. Fans of the current profile are listed; new fans appear at the end."));
            if (fans.Count == 0) { p.Children.Add(Empty(_sampler.ConfigName == null ? "Waiting for Fan Control's configuration…" : "No fan control is enabled in this profile.")); return p; }
            var rows = FanOrder.Apply(fans, _settings.Fans);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string id = r.Fan.ControlId;
                if (id == null) continue;
                string original = string.IsNullOrEmpty(r.Fan.Name) ? id : r.Fan.Name;
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var moves = Moves(r.Name, i > 0 ? () => { FanOrder.Move(_settings, fans, id, -1); SaveAndRefresh(); } : (Action)null,
                                          i < rows.Count - 1 ? () => { FanOrder.Move(_settings, fans, id, +1); SaveAndRefresh(); } : (Action)null);
                DockPanel.SetDock(moves, Dock.Right);
                row.Children.Add(moves);
                var visible = Check("", !r.Hidden, v => _settings.Fan(id).Hidden = !v);
                visible.Margin = new Thickness(0, 0, 4, 0);
                AutomationProperties.SetName(visible, "Show " + original);
                visible.ToolTip = "Show this fan";
                DockPanel.SetDock(visible, Dock.Left);
                row.Children.Add(visible);
                var name = TextField("Name for " + original, r.Name, 200, v =>
                {
                    var pref = _settings.Fan(id);
                    pref.Name = string.IsNullOrWhiteSpace(v) || v.Trim() == original ? null : v.Trim();
                    Save();
                });
                DockPanel.SetDock(name, Dock.Left);
                row.Children.Add(name);
                var orig = DashboardView.Small(r.Name != original ? "Fan Control: " + original : "", Palette.TextMuted);
                orig.Margin = new Thickness(10, 0, 0, 0); orig.TextTrimming = TextTrimming.CharacterEllipsis;
                row.Children.Add(orig);
                p.Children.Add(row);
            }
            return p;
        }

        // ---------------------------------------------------------------- Look

        FrameworkElement LookTab()
        {
            var g = new Form();
            g.Add("Temperature", Left(new Segmented(new[] { "°C", "°F" }, i => { _settings.Fahrenheit = i == 1; Save(); }, "Temperature unit"), _settings.Fahrenheit ? 1 : 0));
            g.Add("Density", Left(new Segmented(new[] { "Comfortable", "Compact" }, i => { _settings.Compact = i == 1; Save(); }, "Density"), _settings.Compact ? 1 : 0));
            int sm = Math.Max(0, Array.IndexOf(Settings.SmoothingSteps, _settings.Smoothing));
            var smooth = Left(new Segmented(new[] { "Off", "3 s", "10 s", "30 s" }, i => { _settings.Smoothing = Settings.SmoothingSteps[i]; Save(); }, "Smooth chart lines"), sm);
            smooth.ToolTip = "Moving average for the chart lines. Cards always show the latest value.";
            g.Add("Smooth lines", smooth);

            var colors = new StackPanel();
            foreach (var row in new[]
            {
                new[] { "CPU", "cpu.temp", "cpu.load" }, new[] { "GPU", "gpu.temp", "gpu.load" }, new[] { "GPU hotspot", "gpu.hot" },
                new[] { "VRAM", "gpu.vram" }, new[] { "RAM", "ram.temp", "ram.load" },
            })
            {
                var keys = row.Skip(1).ToArray();
                string value; _settings.Colors.TryGetValue(keys[0], out value);
                var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var label = DashboardView.Small(row[0]); label.Width = 90; label.Foreground = Palette.Text;
                line.Children.Add(label);
                line.Children.Add(new ColorPicker(row[0], value, Palette.DefaultColor(keys[0], 0), v =>
                {
                    foreach (var k in keys) { if (v == null) _settings.Colors.Remove(k); else _settings.Colors[k] = v; }
                    Save();
                }));
                colors.Children.Add(line);
            }
            colors.Children.Add(Hint("Colours of your own sensors are set on the Custom tab."));
            g.Add("Colours", colors);
            return g.Element;
        }

        // ---------------------------------------------------------------- building blocks

        /// <summary>Two-column form: muted labels on the left, controls on the right.</summary>
        sealed class Form
        {
            readonly Grid _grid = new Grid();
            int _row;
            public double LabelWidth = 110;
            public Thickness Margin { set { _grid.Margin = value; } }
            public FrameworkElement Element
            {
                get
                {
                    if (_grid.ColumnDefinitions.Count == 0)
                    {
                        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
                        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    }
                    return _grid;
                }
            }
            public void Add(string label, UIElement content)
            {
                _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = DashboardView.Small(label); l.Margin = new Thickness(0, 9, 12, 9); l.VerticalAlignment = VerticalAlignment.Top;
                l.TextWrapping = TextWrapping.Wrap;
                Grid.SetRow(l, _row); _grid.Children.Add(l);
                var fe = content as FrameworkElement; if (fe != null) { fe.Margin = new Thickness(0, 4, 0, 4); fe.VerticalAlignment = VerticalAlignment.Center; }
                Grid.SetRow(content, _row); Grid.SetColumn(content, 1); _grid.Children.Add(content);
                AutomationProperties.SetName(content, label);
                _row++;
            }
        }

        sealed class Pick
        {
            public string Text { get; private set; }
            public string Id { get; private set; }
            public Pick(string text, string id) { Text = text; Id = id; }
            public override string ToString() { return Text; }
        }

        /// <summary>"Core (Tctl/Tdie) · amdcpu/0": the sensor name and where it comes from.</summary>
        static string SensorLabel(SensorInfo s)
        {
            var parts = s.Id.Trim('/').Split('/');
            string source;
            if (s.Id.StartsWith("ADLX/", StringComparison.Ordinal)) source = parts.Length > 1 ? parts[1] : "ADLX";
            else if (s.Id.StartsWith("/", StringComparison.Ordinal)) source = parts.Length > 1 ? parts[0] + "/" + parts[1] : parts[0];
            else source = parts[0]; // Fan Control's own mix/custom sensors and plugin sensors
            return s.Name + "  ·  " + source;
        }

        static IEnumerable<SensorInfo> Sorted(IEnumerable<SensorInfo> sensors)
        {
            return sensors.OrderBy(s => s.Id.StartsWith("/", StringComparison.Ordinal) ? 1 : 0)
                          .ThenBy(s => SensorLabel(s).Substring(SensorLabel(s).IndexOf('·') + 1), StringComparer.OrdinalIgnoreCase)
                          .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);
        }

        static string TypeName(string type)
        {
            switch (type)
            {
                case "UsagePercent": return "load";
                case "Rpm": return "speed";
                case "Control": return "fan duty";
                default: return type.ToLowerInvariant();
            }
        }

        bool _refreshQueued;

        /// <summary>The sensor list is read on the sampler thread: show the tab again once it has arrived.</summary>
        void RefreshSoon()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            int tab = _tabs.Selected, count = _sampler.Catalog.Count;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            int tries = 0;
            timer.Tick += (s, e) =>
            {
                if (++tries > 10 || _tabs.Selected != tab || !Element.IsVisible) { timer.Stop(); _refreshQueued = false; return; }
                if (_sampler.Catalog.Count == count && count == 0) return;
                timer.Stop(); _refreshQueued = false;
                ShowTab();
            };
            timer.Start();
        }

        CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 2, 20, 2), VerticalAlignment = VerticalAlignment.Center };
            if (!Theme.Adopted) c.Foreground = Palette.Text; // inside Fan Control the native check box style colours it
            RoutedEventHandler h = (s, e) => { set(c.IsChecked == true); Save(); };
            c.Checked += h; c.Unchecked += h;
            return c;
        }

        CheckBox SmallCheck(string text, bool value, Action<bool> set)
        {
            var c = Check(text, value, set);
            c.FontSize = 12;
            c.Margin = new Thickness(0, 0, 16, 0);
            return c;
        }

        static FrameworkElement Link(string text, Action click)
        {
            var t = new TextBlock { Text = text, FontSize = 12, Foreground = Palette.Accent };
            return new Pressable(text, click, 4) { Child = t, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(-4, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        }

        static TextBlock Hint(string text)
        {
            var t = DashboardView.Small(text, Palette.TextMuted);
            t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 4); t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        static FrameworkElement Empty(string text)
        {
            var t = DashboardView.Small(text, Palette.TextDim);
            t.Margin = new Thickness(0, 12, 0, 4);
            return t;
        }

        static FrameworkElement Left(Segmented seg, int selected)
        {
            seg.Select(selected);
            seg.HorizontalAlignment = HorizontalAlignment.Left;
            return seg;
        }

        static FrameworkElement Inline(string label, FrameworkElement box, string tip)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, ToolTip = tip };
            var l = DashboardView.Small(label); l.Margin = new Thickness(0, 0, 8, 0); l.Foreground = Palette.Text;
            var u = DashboardView.Small(Units.Temp); u.Margin = new Thickness(4, 0, 0, 0);
            p.Children.Add(l); p.Children.Add(box); p.Children.Add(u);
            return p;
        }

        static FrameworkElement Moves(string name, Action up, Action down)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
            var u = new RowButton(GlyphUp, "Move " + name + " up", up ?? (() => { })) { IsEnabled = up != null };
            var d = new RowButton(GlyphDown, "Move " + name + " down", down ?? (() => { })) { IsEnabled = down != null };
            p.Children.Add(u); p.Children.Add(d);
            return p;
        }

        TextBox Field(string name, double width)
        {
            var t = new TextBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
            if (!Theme.Adopted) { t.Foreground = Palette.Text; t.CaretBrush = Palette.Text; t.Background = Brushes.Transparent; t.BorderBrush = Palette.CardEdge; t.Padding = new Thickness(2, 1, 2, 1); }
            AutomationProperties.SetName(t, name);
            return t;
        }

        /// <summary>Enter and leaving the box apply, Esc reverts.</summary>
        static void Commits(TextBox t, Action commit, Action revert)
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

        TextBox TextField(string name, string value, double width, Action<string> set)
        {
            var t = Field(name, width);
            t.Text = value ?? "";
            string last = t.Text;
            Commits(t, () => { if (t.Text != last) { last = t.Text; set(t.Text); } }, () => t.Text = last);
            return t;
        }

        /// <summary>A temperature in the display unit (°C or °F), clamped to the allowed range, stored in °C.</summary>
        TextBox TempBox(string name, double celsius, double minC, double maxC, Action<double> set)
        {
            var t = Field(name + " in " + (Units.Fahrenheit ? "degrees Fahrenheit" : "degrees Celsius"), 48);
            t.TextAlignment = TextAlignment.Right;
            double current = celsius;
            Func<string> show = () => Units.ToDisplay(current).ToString("0", Inv);
            t.Text = show();
            t.ToolTip = string.Format(Inv, "{0:0} to {1:0} {2}. Enter applies, Esc reverts.", Units.ToDisplay(minC), Units.ToDisplay(maxC), Units.Temp);
            Commits(t, () =>
            {
                double v;
                if (double.TryParse(t.Text.Replace(',', '.'), NumberStyles.Float, Inv, out v))
                {
                    current = Math.Round(Math.Max(minC, Math.Min(maxC, Units.FromDisplay(v))));
                    set(current); Save();
                }
                t.Text = show(); // clamped value, or the old one when it was not a number
            }, () => t.Text = show());
            t.PreviewTextInput += (s, e) => { e.Handled = e.Text.Any(ch => !char.IsDigit(ch) && ch != '.' && ch != ','); };
            return t;
        }

        /// <summary>Stretches <paramref name="e"/> with the available width, up to <paramref name="max"/>, aligned left.</summary>
        static FrameworkElement Fit(FrameworkElement e, double max, FrameworkElement after = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = max });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(e);
            if (after != null) { Grid.SetColumn(after, 1); g.Children.Add(after); }
            return g;
        }

        ComboBox Combo(string name, List<Pick> items, string selectedId, Action<string> changed)
        {
            var c = new ComboBox { ItemsSource = items, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left, IsTextSearchEnabled = true, MaxDropDownHeight = 360 };
            TextSearch.SetTextPath(c, "Text");
            if (!Theme.Adopted) c.Foreground = Brushes.Black;
            AutomationProperties.SetName(c, name);
            c.SelectedItem = items.FirstOrDefault(i => i.Id == selectedId) ?? items.FirstOrDefault();
            c.HorizontalAlignment = HorizontalAlignment.Stretch;
            c.SelectionChanged += (s, e) =>
            {
                var p = c.SelectedItem as Pick;
                if (p != null && !_building) changed(p.Id);
            };
            return c;
        }
    }
}
