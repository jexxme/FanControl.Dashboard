using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FanControl.Plugins;

namespace FanControl.Dashboard
{
    /// <summary>
    /// Fan Control plugin: adds a "Dashboard" section (temperatures, load, fans, history charts) to the top of the Home page,
    /// and exposes CPU / GPU / RAM load as sensors for tray icons. Options live in %AppData%\FanControl.Dashboard\settings.json.
    /// Inserting UI is not part of Fan Control's plugin API: the view is found and inserted by type and element names
    /// ("HomePage", "MainPanel"), so a Fan Control update can stop the section from appearing. Fan control itself is never affected.
    /// </summary>
    public sealed class DashboardPlugin : IPlugin2
    {
        readonly IPluginLogger _logger;
        Sampler _sampler;
        Injector _injector;
        readonly ValueSensor _cpuLoad = new ValueSensor("Dashboard/cpu-load", "CPU load %");
        readonly ValueSensor _gpuLoad = new ValueSensor("Dashboard/gpu-load", "GPU load %");
        readonly ValueSensor _ramLoad = new ValueSensor("Dashboard/ram-load", "RAM used %");

        public DashboardPlugin(IPluginLogger logger) { _logger = logger; }

        public string Name { get { return "Dashboard"; } }

        public void Initialize()
        {
            try
            {
                _sampler = new Sampler(Log) { HistoryPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Settings.DefaultPath), "history.bin") };
                _sampler.Start();
                _injector = new Injector(_sampler, Settings.Load(Settings.DefaultPath, Log), Log);
                _injector.Start();
                Log("initialized");
            }
            catch (Exception ex) { Log("initialize failed: " + ex); }
        }

        public void Load(IPluginSensorsContainer container)
        {
            container.TempSensors.Add(_cpuLoad);
            container.TempSensors.Add(_gpuLoad);
            container.TempSensors.Add(_ramLoad);
        }

        public void Update()
        {
            var s = _sampler;
            if (s == null) return;
            _cpuLoad.Set(s.Get("cpu.load"));
            _gpuLoad.Set(s.Get("gpu.load"));
            _ramLoad.Set(s.Get("ram.load"));
        }

        public void Close()
        {
            try { if (_injector != null) _injector.Stop(); } catch (Exception ex) { Log("stop view failed: " + ex.Message); }
            try { if (_sampler != null) _sampler.Stop(); } catch (Exception ex) { Log("stop sampler failed: " + ex.Message); }
            _injector = null; _sampler = null;
        }

        void Log(string message)
        {
            try { if (_logger != null) _logger.Log("[Dashboard] " + message); } catch { }
        }
    }

    sealed class ValueSensor : IPluginSensor
    {
        volatile object _value;
        readonly string _id, _name;
        public ValueSensor(string id, string name) { _id = id; _name = name; }
        public string Id { get { return _id; } }
        public string Name { get { return _name; } }
        public float? Value { get { var v = _value; return v == null ? (float?)null : (float)v; } }
        public void Set(float v) { _value = float.IsNaN(v) ? null : (object)(float)Math.Round(v, 1); }
        public void Update() { }
    }

    /// <summary>Keeps the dashboard view inserted as the first section of the Home page's "MainPanel".</summary>
    sealed class Injector
    {
        readonly Sampler _sampler;
        readonly Settings _settings;
        readonly Action<string> _log;
        Dispatcher _dispatcher;
        DispatcherTimer _timer;
        DashboardView _view;
        WeakReference _home = new WeakReference(null);
        ScrollViewer _hookedScroll;
        int _failures, _themeTries, _themeChecks;

        public Injector(Sampler sampler, Settings settings, Action<string> log) { _sampler = sampler; _settings = settings; _log = log; }

        public void Start()
        {
            var app = Application.Current;
            if (app == null) { _log("no WPF application found, dashboard view disabled"); return; }
            _dispatcher = app.Dispatcher;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1.5) };
                    _timer.Tick += (s, e) => Tick();
                    _timer.Start();
                    Tick();
                }
                catch (Exception ex) { _log("view start failed: " + ex); }
            }));
        }

        public void Stop()
        {
            var d = _dispatcher;
            if (d == null) return;
            var timer = _timer; var view = _view;
            d.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (timer != null) timer.Stop();
                    if (view != null) { view.StopTimer(); var p = view.Parent as Panel; if (p != null) p.Children.Remove(view); }
                }
                catch (Exception ex) { _log("detach failed: " + ex.Message); }
            }));
        }

        void Tick()
        {
            try
            {
                var home = _home.Target as FrameworkElement;
                if (home == null || PresentationSource.FromVisual(home) == null)
                {
                    home = null;
                    foreach (Window w in Application.Current.Windows)
                    {
                        home = Find(w, "HomePage");
                        if (home != null) break;
                    }
                    _home = new WeakReference(home);
                }
                if (home == null) return;
                var panel = home.FindName("MainPanel") as Panel;
                var scroll = home.FindName("ScrollViewer") as ScrollViewer;
                if (panel == null) { if (_failures++ == 0) _log("MainPanel not found on HomePage"); return; }

                if (_view == null)
                {
                    if (!Theme.Adopted && !Theme.Adopt(home, null) && _themeTries++ < 4) return;
                    _view = new DashboardView(_sampler, _settings);
                    _log(Theme.Adopted ? "using Fan Control card style" : "native card style not found, using fallback colours");
                }
                else if (++_themeChecks % 3 == 0 && Theme.SourceChanged() && Theme.Adopt(home, _view))
                {
                    // the user switched Fan Control between light and dark: rebuild the view with the new colours
                    var old = _view;
                    old.StopTimer();
                    var oldParent = old.Parent as Panel; if (oldParent != null) oldParent.Children.Remove(old);
                    _view = new DashboardView(_sampler, _settings);
                    _log("Fan Control theme changed, dashboard restyled");
                }

                if (_view.Parent != panel)
                {
                    var old = _view.Parent as Panel;
                    if (old != null) old.Children.Remove(_view);
                    panel.Children.Insert(0, _view);
                    _log("dashboard attached to Home page");
                }
                else if (panel.Children.IndexOf(_view) != 0)
                {
                    panel.Children.Remove(_view); panel.Children.Insert(0, _view);
                }

                if (scroll != null && !ReferenceEquals(scroll, _hookedScroll))
                {
                    _hookedScroll = scroll;
                    scroll.SizeChanged += (s, e) => Fit(panel, scroll);
                }
                Fit(panel, scroll);
            }
            catch (Exception ex)
            {
                if (_failures++ < 5) _log("attach failed: " + ex);
                if (_failures > 30 && _timer != null) { _timer.Stop(); _log("giving up attaching the dashboard"); }
            }
        }

        void Fit(Panel panel, ScrollViewer scroll)
        {
            if (_view == null) return;
            var wrap = panel as WrapPanel;
            bool columns = wrap != null && wrap.Orientation == Orientation.Horizontal;
            double avail = scroll != null && scroll.ViewportWidth > 0 ? scroll.ViewportWidth : panel.ActualWidth;
            double target = columns ? 600 : Math.Max(320, avail - _view.Margin.Left - _view.Margin.Right - 2);
            if (double.IsNaN(_view.Width) || Math.Abs(_view.Width - target) > 0.5) _view.Width = target;
        }

        static FrameworkElement Find(DependencyObject root, string typeName)
        {
            if (root == null) return null;
            if (root.GetType().Name == typeName) return root as FrameworkElement;
            int n = root is Visual || root is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(root) : 0;
            for (int i = 0; i < n; i++)
            {
                var r = Find(VisualTreeHelper.GetChild(root, i), typeName);
                if (r != null) return r;
            }
            return null;
        }
    }
}
