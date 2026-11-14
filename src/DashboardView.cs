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
    sealed class SeriesDef
    {
        public string Name, Short, Key, Unit; public Brush Brush;
        public SeriesDef(string name, string key, Brush brush, string unit, string shortName = null) { Name = name; Key = key; Brush = brush; Unit = unit; Short = shortName ?? name; }
    }

    /// <summary>The dashboard section that is placed at the top of Fan Control's Home page.</summary>
    public sealed class DashboardView : Border
    {
        public const string Marker = "FanControl.Dashboard.View";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string GlyphSettings = "", GlyphCollapse = "", GlyphExpand = "", GlyphClose = "";

        readonly Sampler _sampler;
        readonly Settings _settings;
        readonly DispatcherTimer _timer;
        readonly TextBlock _subtitle = Small("", Palette.TextMuted);

        readonly TextBlock _cpuDetail = Small("", Palette.TextMuted), _cpuTemp = Big(), _cpuStatus = Small(""), _cpuLoadText = Value(), _cpuPower = Small("");
        readonly TextBlock _gpuDetail = Small("", Palette.TextMuted), _gpuTemp = Big(), _gpuHot = Small(""), _gpuVram = Small(""), _gpuStatus = Small(""), _gpuLoadText = Value();
        readonly FrameworkElement _gpuHotChip, _gpuVramChip;
        FrameworkElement _gpuLoadRow;
        readonly TextBlock _ramDetail = Small("", Palette.TextMuted), _ramTemp = Big(), _ramStatus = Small(""), _ramLoadText = Value(), _ramGb = Small(""), _ramModules = Small("", Palette.TextMuted);
        readonly Bar _cpuLoad = new Bar(Palette.Cpu), _gpuLoad = new Bar(Palette.Gpu), _ramLoad = new Bar(Palette.Ram);
        readonly Grid _fanGrid = new Grid();
        readonly TextBlock _fanEmpty = Small("", Palette.TextMuted);
        readonly List<Tuple<FanInfo, Bar, TextBlock, TextBlock>> _fanRows = new List<Tuple<FanInfo, Bar, TextBlock, TextBlock>>();
        string _fanSignature;

        readonly StackPanel _body = new StackPanel();
        readonly Grid _header = new Grid { Margin = new Thickness(2, 0, 0, 10) };
        bool? _narrowHeader;
        readonly ColumnsPanel _cards = new ColumnsPanel { Gap = 16, MinColumnWidth = 280 };
        readonly ColumnsPanel _charts = new ColumnsPanel { Gap = 16, MinColumnWidth = 440 };
        readonly FrameworkElement _cpuCard, _gpuCard, _ramCard, _fanCard, _settingsCard, _emptyHint;
        readonly ChartCard _tempCard, _loadCard, _singleCard;
        readonly Segmented _range;
        readonly IconButton _gear, _collapse;
        bool _settingsOpen;

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
            _range = new Segmented(Settings.Ranges.Select(m => m + " min").ToArray(), i => { _settings.RangeMinutes = Settings.Ranges[i]; _settings.Save(); }, "History length");
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
            SizeChanged += (s, e) => LayoutHeader(e.NewSize.Width < 620);

            _cpuCard = CpuCard(); _gpuCard = GpuCard(out _gpuHotChip, out _gpuVramChip); _ramCard = RamCard(); _fanCard = FanCard();
            _settingsCard = SettingsCard();
            _settingsCard.Margin = new Thickness(0, 0, 0, 16);
            _emptyHint = EmptyHint();

            _tempCard = new ChartCard("Temperature", "°C", 0, _settings, false, OnHover);
            _loadCard = new ChartCard("Load", "%", 1, _settings, false, OnHover);
            _singleCard = new ChartCard("History", null, _settings.SingleMode, _settings, true, OnHover);

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(_body);
            _body.Children.Add(_settingsCard);
            _body.Children.Add(_emptyHint);
            _body.Children.Add(_cards);
            _body.Children.Add(_charts);
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
        public void Debug(int layout, int range, double hover, bool settingsOpen)
        {
            _settings.Layout = (ChartLayout)Math.Max(0, Math.Min(2, layout));
            _settings.RangeMinutes = Settings.Ranges[Math.Max(0, Math.Min(Settings.Ranges.Length - 1, range))];
            _settingsOpen = settingsOpen;
            _settings.Save();
            Refresh();
            if (hover >= 0) { var c = _settings.Layout == ChartLayout.Single ? _singleCard.Chart : _tempCard.Chart; c.Hover(hover); OnHover(c, hover); }
        }

        // ---------- settings ----------

        void ToggleSettings()
        {
            _settingsOpen = !_settingsOpen;
            if (_settingsOpen && _settings.Collapsed) { _settings.Collapsed = false; _settings.Save(); }
            else ApplySettings();
        }

        void ApplySettings()
        {
            bool collapsed = _settings.Collapsed;
            _body.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            _collapse.Glyph = collapsed ? GlyphExpand : GlyphCollapse;
            _collapse.Tip = collapsed ? "Expand the dashboard" : "Collapse the dashboard";
            _range.Visibility = collapsed || !_settings.ShowCharts ? Visibility.Collapsed : Visibility.Visible;
            _settingsCard.Visibility = _settingsOpen && !collapsed ? Visibility.Visible : Visibility.Collapsed;
            _gear.Active = _settingsOpen && !collapsed;
            _range.Select(Math.Max(0, Array.IndexOf(Settings.Ranges, _settings.RangeMinutes)));
            SyncSettingsControls();

            _cards.Children.Clear();
            if (_settings.ShowCpu) _cards.Children.Add(_cpuCard);
            if (_settings.ShowGpu) _cards.Children.Add(_gpuCard);
            if (_settings.ShowRam) _cards.Children.Add(_ramCard);
            if (_settings.ShowFans) _cards.Children.Add(_fanCard);
            _cards.Visibility = _cards.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _emptyHint.Visibility = _cards.Children.Count == 0 && !_settings.ShowCharts && !_settingsOpen ? Visibility.Visible : Visibility.Collapsed;

            _charts.Children.Clear();
            _charts.Visibility = _settings.ShowCharts ? Visibility.Visible : Visibility.Collapsed;
            _charts.Margin = new Thickness(0, _cards.Children.Count > 0 ? 16 : 0, 0, 0);
            if (_settings.Layout == ChartLayout.Single) _charts.Children.Add(_singleCard.Element);
            else { _charts.Children.Add(_tempCard.Element); _charts.Children.Add(_loadCard.Element); }
            _charts.ForceSingleColumn = _settings.Layout == ChartLayout.Stacked;
            _charts.InvalidateMeasure();

            int points = _settings.RangeMinutes * 60;
            foreach (var c in new[] { _tempCard, _loadCard, _singleCard }) c.Apply(points);
            _singleCard.SetMode(_settings.SingleMode);
            SafeRefresh();
        }

        CheckBox _chkCpu, _chkGpu, _chkRam, _chkFans, _chkCharts, _chkLimits, _chkRamAuto;
        Segmented _layoutSeg, _heightSeg;
        TextBox _cpuLimitBox, _ramWarnBox;
        TextButton _reset;
        TextBlock _ramAutoNote;
        FrameworkElement _ramRow;
        bool _ramRowSeen;
        bool _syncing;

        FrameworkElement SettingsCard()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto), MinWidth = 96 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            Action<string, UIElement> add = (label, content) =>
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = Small(label); l.Margin = new Thickness(0, 9, 16, 9); l.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(l, row); grid.Children.Add(l);
                var fe = content as FrameworkElement; if (fe != null) { fe.Margin = new Thickness(0, 4, 0, 4); fe.VerticalAlignment = VerticalAlignment.Center; }
                Grid.SetRow(content, row); Grid.SetColumn(content, 1); grid.Children.Add(content);
                row++;
            };

            var shown = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            _chkCpu = Check("CPU", v => _settings.ShowCpu = v); _chkGpu = Check("GPU", v => _settings.ShowGpu = v);
            _chkRam = Check("RAM", v => _settings.ShowRam = v); _chkFans = Check("Fans", v => _settings.ShowFans = v);
            foreach (var c in new[] { _chkCpu, _chkGpu, _chkRam, _chkFans }) shown.Children.Add(c);
            add("Cards", shown);

            var charts = new WrapPanel();
            _chkCharts = Check("Show history charts", v => _settings.ShowCharts = v);
            charts.Children.Add(_chkCharts);
            add("History", charts);
            _layoutSeg = new Segmented(new[] { "Side by side", "Stacked", "One chart" }, i => { if (_syncing) return; _settings.Layout = (ChartLayout)i; _settings.Save(); }, "Chart layout");
            add("Layout", Left(_layoutSeg));
            _heightSeg = new Segmented(new[] { "Small", "Medium", "Large" }, i => { if (_syncing) return; _settings.ChartHeight = Settings.Heights[i]; _settings.Save(); }, "Chart height");
            add("Chart height", Left(_heightSeg));

            var limits = new StackPanel();
            _chkLimits = Check("Show limit lines in the temperature chart", v => _settings.ShowLimits = v);
            limits.Children.Add(_chkLimits);
            var cpuRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            _cpuLimitBox = NumberBox("CPU limit", Settings.CpuLimitMin, Settings.CpuLimitMax, v => _settings.CpuLimit = v);
            cpuRow.Children.Add(Inline("CPU limit", _cpuLimitBox, "Your CPU's maximum temperature (Tjmax): about 95 °C for AMD Ryzen 7000/9000, 89 °C for Ryzen X3D, 100 °C for most Intel desktop CPUs."));
            limits.Children.Add(cpuRow);
            var ramRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _ramWarnBox = NumberBox("RAM warning", Settings.RamWarningMin, Settings.RamWarningMax, v => _settings.RamWarning = v);
            ramRow.Children.Add(Inline("RAM warning", _ramWarnBox, "Memory module temperature to warn at."));
            _chkRamAuto = Check("Use the modules' own limit", v => _settings.RamWarningAuto = v);
            _chkRamAuto.ToolTip = "DDR5 modules report a \"high\" temperature threshold (usually 55 °C). Use it when available.";
            _chkRamAuto.Margin = new Thickness(0, 0, 10, 0);
            ramRow.Children.Add(_chkRamAuto);
            _ramAutoNote = Small("", Palette.TextMuted);
            ramRow.Children.Add(_ramAutoNote);
            limits.Children.Add(ramRow);
            _ramRow = ramRow;
            add("Limits", limits);

            var foot = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            _reset = new TextButton("Reset to defaults", () => { _settings.Reset(); _settings.Save(); });
            _reset.ToolTip = "Restore every dashboard option";
            DockPanel.SetDock(_reset, Dock.Right);
            foot.Children.Add(_reset);
            var hint = Small("Click a legend entry to hide or show a line. Changes are saved automatically.", Palette.TextMuted);
            hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 0, 12, 0);
            foot.Children.Add(hint);

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var close = new IconButton(GlyphClose, "Close settings", ToggleSettings) { Width = 26, Height = 26, Margin = new Thickness(0, -4, -6, 0) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(Label("Dashboard settings"));

            var s = new StackPanel();
            s.Children.Add(head);
            s.Children.Add(grid);
            s.Children.Add(Divider());
            s.Children.Add(foot);
            var card = Card(s);
            card.KeyDown += (o, e) => { if (e.Key == Key.Escape && _settingsOpen) { ToggleSettings(); _gear.Focus(); e.Handled = true; } };
            return card;
        }

        void SyncSettingsControls()
        {
            if (_chkCpu == null) return;
            _syncing = true;
            try
            {
                _chkCpu.IsChecked = _settings.ShowCpu; _chkGpu.IsChecked = _settings.ShowGpu; _chkRam.IsChecked = _settings.ShowRam;
                _chkFans.IsChecked = _settings.ShowFans; _chkCharts.IsChecked = _settings.ShowCharts; _chkLimits.IsChecked = _settings.ShowLimits;
                _chkRamAuto.IsChecked = _settings.RamWarningAuto;
                _layoutSeg.Select((int)_settings.Layout);
                int h = Array.IndexOf(Settings.Heights, _settings.ChartHeight); _heightSeg.Select(h < 0 ? 1 : h);
                _layoutSeg.IsEnabled = _heightSeg.IsEnabled = _settings.ShowCharts;
                if (!_cpuLimitBox.IsKeyboardFocused) _cpuLimitBox.Text = _settings.CpuLimit.ToString("0", Inv);
                if (!_ramWarnBox.IsKeyboardFocused) _ramWarnBox.Text = _settings.RamWarning.ToString("0", Inv);
                _reset.IsEnabled = !_settings.IsDefault;
                SyncRamAuto();
            }
            finally { _syncing = false; }
        }

        /// <summary>The manual RAM box is only used when auto is off or the modules report no limit.</summary>
        void SyncRamAuto()
        {
            if (_ramWarnBox == null) return;
            // the warning only matters where modules report a temperature (DDR5); keep it visible until the sensors are known
            _ramRow.Visibility = _sampler.Discovered && float.IsNaN(_sampler.Get("ram.temp")) && !_ramRowSeen ? Visibility.Collapsed : Visibility.Visible;
            if (!float.IsNaN(_sampler.Get("ram.temp"))) _ramRowSeen = true;
            float moduleLimit = _sampler.Get("ram.limit");
            bool known = !float.IsNaN(moduleLimit);
            bool auto = _settings.RamWarningAuto && known;
            _ramWarnBox.IsEnabled = !auto;
            _ramAutoNote.Text = !_settings.RamWarningAuto ? "" : known ? string.Format(Inv, "({0:0} °C reported)", moduleLimit) : "(not reported, using the value)";
            if (auto && !_ramWarnBox.IsKeyboardFocused) _ramWarnBox.Text = moduleLimit.ToString("0", Inv);
            else if (!_ramWarnBox.IsKeyboardFocused) _ramWarnBox.Text = _settings.RamWarning.ToString("0", Inv);
        }

        CheckBox Check(string text, Action<bool> set)
        {
            var c = new CheckBox { Content = text, Margin = new Thickness(0, 2, 20, 2), VerticalAlignment = VerticalAlignment.Center };
            if (!Theme.Adopted) c.Foreground = Palette.Text; // inside Fan Control the native check box style colours it
            RoutedEventHandler h = (s, e) => { if (_syncing) return; set(c.IsChecked == true); _settings.Save(); };
            c.Checked += h; c.Unchecked += h;
            return c;
        }

        TextBox NumberBox(string name, double min, double max, Action<double> set)
        {
            var t = new TextBox { Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (!Theme.Adopted) { t.Foreground = Palette.Text; t.CaretBrush = Palette.Text; t.Background = Brushes.Transparent; t.BorderBrush = Palette.CardEdge; t.Padding = new Thickness(2, 1, 2, 1); }
            AutomationProperties.SetName(t, name + " in degrees Celsius");
            t.ToolTip = string.Format(Inv, "{0:0} to {1:0} °C. Enter applies, Esc reverts.", min, max);
            Action commit = () =>
            {
                double v;
                if (double.TryParse(t.Text.Replace(',', '.'), NumberStyles.Float, Inv, out v))
                {
                    v = Math.Round(Math.Max(min, Math.Min(max, v)));
                    t.Text = v.ToString("0", Inv); // show the clamped value even while the box keeps focus
                    set(v); _settings.Save();
                }
                else SyncSettingsControls(); // not a number: show the current value again
            };
            t.LostKeyboardFocus += (s, e) => commit();
            t.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { commit(); t.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Escape) { t.Text = ""; Keyboard.ClearFocus(); SyncSettingsControls(); e.Handled = true; }
            };
            t.GotKeyboardFocus += (s, e) => t.SelectAll();
            t.PreviewTextInput += (s, e) => { e.Handled = e.Text.Any(ch => !char.IsDigit(ch) && ch != '.' && ch != ','); };
            return t;
        }

        static FrameworkElement Inline(string label, FrameworkElement box, string tip)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, ToolTip = tip };
            var l = Small(label); l.Margin = new Thickness(0, 0, 8, 0); l.Foreground = Palette.Text;
            var u = Small("°C"); u.Margin = new Thickness(4, 0, 0, 0);
            p.Children.Add(l); p.Children.Add(box); p.Children.Add(u);
            return p;
        }

        FrameworkElement EmptyHint()
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            var t = Small("Every card and chart is hidden.", Palette.TextDim); t.Margin = new Thickness(0, 0, 12, 0);
            p.Children.Add(t);
            p.Children.Add(new TextButton("Choose what to show", ToggleSettings));
            return Card(p);
        }

        static FrameworkElement Left(FrameworkElement e) { e.HorizontalAlignment = HorizontalAlignment.Left; return e; }

        static FrameworkElement Divider() { return new Rectangle { Height = 1, Fill = Palette.Grid, Margin = new Thickness(0, 10, 0, 0), SnapsToDevicePixels = true }; }

        void OnHover(Chart source, double fraction)
        {
            foreach (var c in new[] { _tempCard, _loadCard, _singleCard })
                if (!ReferenceEquals(c.Chart, source)) c.Chart.Sync(fraction);
        }

        // ---------- cards ----------

        FrameworkElement CpuCard()
        {
            var s = new StackPanel();
            s.Children.Add(Header("CPU", _cpuDetail, Palette.Cpu));
            s.Children.Add(_cpuTemp);
            s.Children.Add(_cpuStatus);
            s.Children.Add(LoadRow(_cpuLoad, _cpuLoadText, "Load", "Total CPU load"));
            _cpuPower.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(_cpuPower);
            return Card(s);
        }

        FrameworkElement GpuCard(out FrameworkElement hotChip, out FrameworkElement vramChip)
        {
            var s = new StackPanel();
            s.Children.Add(Header("GPU", _gpuDetail, Palette.Gpu));
            s.Children.Add(_gpuTemp);
            var temps = new WrapPanel();
            hotChip = Chip(Palette.Hotspot, _gpuHot); vramChip = Chip(Palette.Vram, _gpuVram);
            temps.Children.Add(hotChip); temps.Children.Add(vramChip); temps.Children.Add(_gpuStatus);
            s.Children.Add(temps);
            _gpuLoadRow = LoadRow(_gpuLoad, _gpuLoadText, "Load", "Busiest GPU engine (3D, compute, video…), the same number Task Manager shows");
            s.Children.Add(_gpuLoadRow);
            return Card(s);
        }

        FrameworkElement RamCard()
        {
            var s = new StackPanel();
            s.Children.Add(Header("RAM", _ramDetail, Palette.Ram));
            s.Children.Add(_ramTemp);
            s.Children.Add(_ramStatus);
            s.Children.Add(LoadRow(_ramLoad, _ramLoadText, "Used", "Physical memory in use"));
            _ramGb.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(_ramGb);
            _ramModules.TextTrimming = TextTrimming.CharacterEllipsis; _ramModules.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(_ramModules);
            return Card(s);
        }

        FrameworkElement FanCard()
        {
            var s = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var unit = Small("duty · speed", Palette.TextMuted); DockPanel.SetDock(unit, Dock.Right);
            head.Children.Add(unit); head.Children.Add(Label("Fans"));
            s.Children.Add(head);
            // names get the room they need (up to about half the card); the bar takes the rest
            _fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 150 });
            _fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 24 });
            _fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            _fanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            s.Children.Add(_fanGrid);
            _fanEmpty.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(_fanEmpty);
            return Card(s);
        }

        void RebuildFans(List<FanInfo> fans)
        {
            string sig = string.Join("|", fans.Select(f => f.ControlId + "=" + f.Name + "=" + f.Curve));
            if (sig == _fanSignature) return;
            _fanSignature = sig;
            _fanGrid.Children.Clear(); _fanGrid.RowDefinitions.Clear(); _fanRows.Clear();
            for (int i = 0; i < fans.Count; i++)
            {
                _fanGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
                var name = new TextBlock { Text = string.IsNullOrEmpty(fans[i].Name) ? fans[i].ControlId : fans[i].Name, Foreground = Palette.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
                name.ToolTip = string.IsNullOrEmpty(fans[i].Curve) ? name.Text : name.Text + "\nCurve: " + fans[i].Curve;
                var bar = new Bar(Palette.Accent) { VerticalAlignment = VerticalAlignment.Center };
                var pct = Value(); pct.HorizontalAlignment = HorizontalAlignment.Right;
                var rpm = Small(""); rpm.HorizontalAlignment = HorizontalAlignment.Right; rpm.VerticalAlignment = VerticalAlignment.Center;
                Place(name, i, 0); Place(bar, i, 1); Place(pct, i, 2); Place(rpm, i, 3);
                _fanRows.Add(Tuple.Create(fans[i], bar, pct, rpm));
            }
            _fanEmpty.Text = fans.Count > 0 ? "" : _sampler.ConfigName == null
                ? "Waiting for Fan Control's configuration…"
                : "No fan control is enabled in this profile. Enable one on Fan Control's Controls page to see it here.";
            _fanEmpty.Visibility = fans.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void Place(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); _fanGrid.Children.Add(e); }

        // ---------- refresh ----------

        void SafeRefresh()
        {
            try { Refresh(); } catch { /* never take Fan Control down */ }
        }

        void Refresh()
        {
            // CPU
            float cpuT = _sampler.Get("cpu.temp"), cpuL = _sampler.Get("cpu.load"), cpuP = _sampler.Get("cpu.power");
            _cpuDetail.Text = _sampler.SensorName("cpu.temp") ?? "";
            SetBig(_cpuTemp, cpuT, "°C");
            Headroom(_cpuStatus, cpuT, _settings.CpuLimit, "limit", 5, "No CPU temperature sensor found");
            _cpuLoad.Value = cpuL; _cpuLoadText.Text = Fmt(cpuL, "0", " %");
            _cpuPower.Text = float.IsNaN(cpuP) ? "" : string.Format(Inv, "Package power {0:0} W", cpuP);
            _cpuPower.Visibility = float.IsNaN(cpuP) ? Visibility.Collapsed : Visibility.Visible;

            // GPU
            float gT = _sampler.Get("gpu.temp"), gH = _sampler.Get("gpu.hot"), gV = _sampler.Get("gpu.vram"), gL = _sampler.Get("gpu.load");
            _gpuDetail.Text = _sampler.SensorName("gpu.temp") ?? "";
            SetBig(_gpuTemp, gT, "°C");
            _gpuHot.Text = "Hotspot " + Fmt(gH, "0", " °C");
            _gpuVram.Text = "VRAM " + Fmt(gV, "0", " °C");
            _gpuHotChip.Visibility = float.IsNaN(gH) ? Visibility.Collapsed : Visibility.Visible;
            _gpuVramChip.Visibility = float.IsNaN(gV) ? Visibility.Collapsed : Visibility.Visible;
            bool noGpu = _sampler.Discovered && float.IsNaN(gT) && float.IsNaN(gH) && float.IsNaN(gL);
            Status(_gpuStatus, noGpu ? "No GPU sensors found" : float.IsNaN(gT) && _sampler.Discovered ? "No GPU temperature sensor found" : "", Palette.TextMuted);
            _gpuStatus.Margin = new Thickness(0, noGpu ? 6 : 0, 0, 0);
            _gpuStatus.Visibility = _gpuStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _gpuTemp.Visibility = _gpuLoadRow.Visibility = noGpu ? Visibility.Collapsed : Visibility.Visible;
            _gpuLoad.Value = gL; _gpuLoadText.Text = Fmt(gL, "0", " %");

            // RAM: module temperature when the modules report one (DDR5), otherwise the amount in use
            float rT = _sampler.Get("ram.temp"), rL = _sampler.Get("ram.load"), rU = _sampler.Get("ram.used"), rTot = _sampler.Get("ram.total");
            double ramWarn = _settings.EffectiveRamWarning(_sampler.Get("ram.limit"));
            var names = _sampler.DimmNames;
            if (!float.IsNaN(rT))
            {
                _ramDetail.Text = names.Count > 1 ? "hottest module" : "module";
                SetBig(_ramTemp, rT, "°C");
                Headroom(_ramStatus, rT, ramWarn, "warning", 3, null);
                _ramGb.Text = float.IsNaN(rU) ? "" : string.Format(Inv, "{0:0.0} of {1:0} GB in use", rU, rTot);
            }
            else
            {
                _ramDetail.Text = "in use";
                SetBig(_ramTemp, rU, "GB", "0.0");
                Status(_ramStatus, float.IsNaN(rTot) ? "" : string.Format(Inv, "of {0:0} GB", rTot), Palette.TextDim);
                _ramGb.Text = _sampler.Discovered ? "The modules report no temperature" : "";
                _ramGb.Foreground = Palette.TextMuted;
            }
            if (!float.IsNaN(rT)) _ramGb.Foreground = Palette.TextDim;
            _ramLoad.Value = rL; _ramLoadText.Text = Fmt(rL, "0", " %");
            var parts = new List<string>();
            if (names.Count > 1)
                for (int d = 0; d < names.Count; d++) { float v = _sampler.Get("ram.dimm" + d); if (!float.IsNaN(v)) parts.Add(names[d] + " " + v.ToString("0", Inv) + " °C"); }
            _ramModules.Text = string.Join(" · ", parts);
            _ramModules.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // fans
            RebuildFans(_sampler.Fans);
            foreach (var r in _fanRows)
            {
                float duty = r.Item1.ControlId == null ? float.NaN : _sampler.Get("duty:" + r.Item1.ControlId);
                float rpm = r.Item1.RpmId == null ? float.NaN : _sampler.Get("rpm:" + r.Item1.RpmId);
                r.Item2.Value = duty; r.Item3.Text = Fmt(duty, "0", " %");
                r.Item4.Text = r.Item1.RpmId == null ? "" : Fmt(rpm, "0", " rpm");
            }

            UpdateSubtitle(cpuT, gT, rT);
            foreach (var c in new[] { _tempCard, _loadCard, _singleCard }) c.SyncAvailability(_sampler);
            if (_settingsOpen) SyncRamAuto();
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
                if (!float.IsNaN(cpuT)) bits.Add("CPU " + cpuT.ToString("0", Inv) + " °C");
                if (!float.IsNaN(gpuT)) bits.Add("GPU " + gpuT.ToString("0", Inv) + " °C");
                if (!float.IsNaN(ramT)) bits.Add("RAM " + ramT.ToString("0", Inv) + " °C");
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

        /// <summary>"24 °C below the 95 °C limit", amber within <paramref name="warnWithin"/> degrees, red at or over it.</summary>
        static void Headroom(TextBlock t, float value, double limit, string word, double warnWithin, string missing)
        {
            if (float.IsNaN(value)) { Status(t, missing ?? "", Palette.TextMuted); return; }
            double d = Math.Round(limit) - Math.Round(value);
            if (d > warnWithin) Status(t, string.Format(Inv, "{0:0} °C below the {1:0} °C {2}", d, limit, word), Palette.TextDim);
            else if (d > 0) Status(t, string.Format(Inv, "{0:0} °C below the {1:0} °C {2}", d, limit, word), Palette.Warn);
            else if (d == 0) Status(t, string.Format(Inv, "At the {0:0} °C {1}", limit, word), Palette.Critical);
            else Status(t, string.Format(Inv, "{0:0} °C above the {1:0} °C {2}", -d, limit, word), Palette.Critical);
        }

        static void Status(TextBlock t, string text, Brush b) { t.Text = text; t.Foreground = b; }

        static string Fmt(float v, string f, string unit) { return float.IsNaN(v) ? "–" : v.ToString(f, Inv) + unit; }

        static void SetBig(TextBlock t, float v, string unit, string format = "0")
        {
            t.Inlines.Clear();
            t.Inlines.Add(new Run(float.IsNaN(v) ? "–" : v.ToString(format, Inv)));
            t.Inlines.Add(new Run(" " + unit) { FontSize = 14, Foreground = Palette.TextDim });
        }

        // ---------- small building blocks ----------

        internal static FrameworkElement Card(UIElement child)
        {
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
                    c.Padding = new Thickness(16, 12, 16, 14);
                    c.Content = child;
                    return c;
                }
                catch { }
            }
            return new Border { Background = Palette.Card, CornerRadius = new CornerRadius(4), Padding = new Thickness(16, 12, 16, 14), Child = child };
        }

        static FrameworkElement Header(string text, TextBlock detail, Brush swatch)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            p.Children.Add(Swatch(swatch));
            p.Children.Add(Label(text));
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
        static TextBlock Big() { return new TextBlock { FontSize = 26, FontWeight = FontWeights.Normal, Foreground = Palette.Text, Margin = new Thickness(0, 0, 0, 2) }; }

        internal static FrameworkElement Swatch(Brush b, bool hollow = false)
        {
            return new Ellipse { Width = 8, Height = 8, Fill = hollow ? null : b, Stroke = hollow ? b : null, StrokeThickness = 1.5, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        }

        static FrameworkElement LoadRow(Bar bar, TextBlock text, string label, string tip)
        {
            var g = new Grid { Margin = new Thickness(0, 10, 0, 0), Height = 18, Background = Brushes.Transparent, ToolTip = tip };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
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
            Chart.Mode = mode; _toggle.Select(mode); _unit.Text = mode == 0 ? "°C" : "%";
            UpdateLegend(); Chart.InvalidateVisual();
        }

        /// <summary>Lists only series that have (had) data, once Fan Control's sensors are known.</summary>
        public void SyncAvailability(Sampler sampler)
        {
            _discovered = sampler.Discovered;
            foreach (var key in Sampler.HistoryKeys)
                if (!float.IsNaN(sampler.Get(key))) _seen.Add(key);
            string sig = _discovered + ":" + Chart.Mode + ":" + string.Join(",", Chart.AllSeries.Where(d => _seen.Contains(d.Key)).Select(d => d.Key));
            if (sig != _legendSignature) UpdateLegend();
        }

        bool Available(SeriesDef d) { return !_discovered || _seen.Contains(d.Key); }

        void UpdateLegend()
        {
            _legendSignature = _discovered + ":" + Chart.Mode + ":" + string.Join(",", Chart.AllSeries.Where(d => _seen.Contains(d.Key)).Select(d => d.Key));
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
