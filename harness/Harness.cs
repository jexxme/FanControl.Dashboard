using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FanControl.Dashboard;

// Stand-alone host for the dashboard view: renders it to a PNG without a Fan Control window.
//
//   Harness.exe [options]
//     --out <file.png>        output file (default dash.png)
//     --demo [scenario]       synthetic data instead of Fan Control's live sensors.
//                             scenarios: full (default), intel-nvidia, ddr4, no-gpu, no-fans, hot, starting
//     --live                  live data over Fan Control's IPC (Fan Control must be running)
//     --seconds <n>           live mode: how long to sample before rendering (default 8)
//     --theme <dark|light|plain>  dark/light host a MaterialDesign card like Fan Control does (needs MaterialDesignThemes.Wpf.dll
//                             from FANCONTROL_DIR); plain uses the built-in fallback colours (default dark)
//     --width <px>            view width (default 1180)
//     --layout <0|1|2>        side by side | stacked | one chart
//     --range <0..3>          2 / 5 / 10 / 30 minutes
//     --hover <0..1>          show the hover crosshair/tooltip at this fraction of the chart
//     --settings              open the settings panel
//     --collapsed             collapse the section
//     --hide <keys>           comma-separated legend entries to hide, e.g. 0:gpu.vram
//
// Fan Control's assemblies are loaded from FANCONTROL_DIR or C:\Program Files (x86)\FanControl.
static class Harness
{
    static readonly string App = Environment.GetEnvironmentVariable("FANCONTROL_DIR") ?? @"C:\Program Files (x86)\FanControl";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [STAThread]
    static int Main(string[] a)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var f = Path.Combine(App, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(f) ? Assembly.LoadFrom(f) : null;
        };
        try { return Run(a); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static string Opt(string[] a, string name, string def)
    {
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal) ? a[i + 1] : def;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string[] a)
    {
        string png = Opt(a, "--out", "dash.png");
        bool live = a.Contains("--live");
        string scenario = Opt(a, "--demo", "full");
        int seconds = int.Parse(Opt(a, "--seconds", live ? "8" : "0"), Inv);
        string theme = Opt(a, "--theme", "dark");
        double width = double.Parse(Opt(a, "--width", "1180"), Inv);
        int layout = int.Parse(Opt(a, "--layout", "0"), Inv);
        int range = int.Parse(Opt(a, "--range", "2"), Inv);
        double hover = double.Parse(Opt(a, "--hover", "-1"), Inv);
        bool settingsOpen = a.Contains("--settings");
        bool collapsed = a.Contains("--collapsed");
        string hide = Opt(a, "--hide", null);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        bool light = theme == "light";
        Color page = theme == "plain" ? Color.FromRgb(0x2B, 0x2B, 0x2B) : light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);
        if (theme != "plain") LoadMaterialDesign(app, light);

        var sampler = new Sampler(Console.WriteLine);
        if (live) sampler.Start();
        else Demo.Fill(sampler, scenario);

        var settings = new Settings { Collapsed = collapsed };
        if (hide != null) settings.HiddenSeries.AddRange(hide.Split(','));
        var host = new StackPanel { Background = new SolidColorBrush(page), Width = width + 24 };
        var win = new Window
        {
            // the content sits in a Canvas, which lays it out at its full size: a window cannot grow past the screen,
            // and anything taller would be clipped in the PNG (e.g. on CI runners with small screens)
            Title = "Dashboard harness", Width = 200, Height = 200, Left = -10000, Top = 0,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI"), Content = new Canvas { Children = { host } }, Background = new SolidColorBrush(page),
        };
        if (theme != "plain")
        {
            // like Fan Control's MaterialDesign window: body text and page colours come from the theme
            var fg = app.TryFindResource("MaterialDesign.Brush.Foreground") ?? app.TryFindResource("MaterialDesignBody");
            var bg = app.TryFindResource("MaterialDesign.Brush.Background") ?? app.TryFindResource("MaterialDesignPaper");
            if (fg is Brush) win.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, fg);
            if (bg is Brush) { win.Background = (Brush)bg; host.Background = (Brush)bg; }
            // a native-looking card plus a section header, like the ones Theme.Adopt copies from Fan Control's Home page
            var sample = MaterialCard(light);
            if (sample != null) host.Children.Add(sample);
        }

        win.Loaded += (s, e) =>
        {
            if (theme != "plain")
            {
                Theme.Adopt(host, null);
                host.Children.Clear();
                if (a.Contains("--verbose")) Console.WriteLine("adopted: card " + Palette.Card + " text " + Palette.Text + " dim " + Palette.TextDim + " light " + Palette.IsLight);
            }
            var view = new DashboardView(sampler, settings) { Width = width };
            host.Children.Add(view);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.3, seconds)) };
            timer.Tick += (o, ev) =>
            {
                timer.Stop();
                view.Debug(layout, range, hover, settingsOpen);
                win.UpdateLayout();
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    // lay out at full height: a window cannot grow past the screen, which would clip tall renders (e.g. on CI)
                    var rtb = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(host);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(png)) enc.Save(fs);
                    Console.WriteLine("saved " + png + " " + host.ActualWidth + "x" + host.ActualHeight);
                    if (live) sampler.Stop();
                    app.Shutdown();
                }), DispatcherPriority.ApplicationIdle);
            };
            timer.Start();
        };
        app.Run(win);
        return 0;
    }

    /// <summary>MaterialDesign (MIT) theme like Fan Control uses, so check boxes, text boxes and cards look native.</summary>
    static void LoadMaterialDesign(Application app, bool light)
    {
        if (!File.Exists(Path.Combine(App, "MaterialDesignThemes.Wpf.dll"))) { Console.WriteLine("MaterialDesignThemes.Wpf.dll not found, using fallback colours"); return; }
        Assembly.LoadFrom(Path.Combine(App, "MaterialDesignThemes.Wpf.dll"));
        Assembly.LoadFrom(Path.Combine(App, "MaterialDesignColors.dll"));
        string xaml =
            "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:md='http://materialdesigninxaml.net/winfx/xaml/themes'>" +
            "<ResourceDictionary.MergedDictionaries>" +
            "<md:BundledTheme BaseTheme='" + (light ? "Light" : "Dark") + "' PrimaryColor='Blue' SecondaryColor='LightBlue' />" +
            "<ResourceDictionary Source='pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml' />" +
            "</ResourceDictionary.MergedDictionaries></ResourceDictionary>";
        try { app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml)); }
        catch (Exception ex) { Console.WriteLine("MaterialDesign theme not loaded: " + ex.Message); }
    }

    static FrameworkElement MaterialCard(bool light)
    {
        var type = Type.GetType("MaterialDesignThemes.Wpf.Card, MaterialDesignThemes.Wpf");
        if (type == null) return null;
        var card = (ContentControl)Activator.CreateInstance(type);
        card.Width = 200; card.Height = 60;
        card.Content = new TextBlock { Text = "Sample card" };
        var header = new TextBlock { Text = "Controls", FontSize = 16, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(light ? Color.FromRgb(0x20, 0x20, 0x20) : Colors.White) };
        var p = new StackPanel();
        p.Children.Add(header); p.Children.Add(card);
        return p;
    }
}

/// <summary>Synthetic sensor data for screenshots and UI checks. Deterministic, contains nothing about a real machine.</summary>
static class Demo
{
    public static void Fill(Sampler sampler, string scenario)
    {
        bool intel = scenario == "intel-nvidia", ddr4 = scenario == "ddr4" || intel, noGpu = scenario == "no-gpu", noFans = scenario == "no-fans", hot = scenario == "hot";
        bool starting = scenario == "starting";
        var fans = noFans ? new List<FanInfo>() : new List<FanInfo>
        {
            new FanInfo { Name = "CPU cooler", ControlId = "c0", RpmId = "r0", Curve = "CPU curve" },
            new FanInfo { Name = "Front intake", ControlId = "c1", RpmId = "r1", Curve = "Case mix" },
            new FanInfo { Name = "Rear exhaust", ControlId = "c2", RpmId = "r2", Curve = "Case mix" },
            new FanInfo { Name = "Top exhaust", ControlId = "c3", RpmId = "r3", Curve = "Case mix" },
            new FanInfo { Name = noGpu ? "Pump" : "GPU", ControlId = "c4", RpmId = noGpu ? "r4" : null, Curve = noGpu ? "Fixed" : "GPU curve" },
        };
        var dimms = ddr4 ? new List<string>() : new List<string> { "DIMM #1", "DIMM #2" };
        var names = new Dictionary<string, string> { { "cpu.temp", intel ? "Package" : "Tctl/Tdie" } };
        if (!noGpu) names["gpu.temp"] = intel ? "core" : "edge";
        string gpuName = noGpu ? null : intel ? null : "AMD Radeon RX 7800 XT";
        int samples = starting ? 40 : Sampler.Capacity;
        var rnd = new Random(7);
        double cpuT = 48, gpuT = 40, hotT = 52, vramT = 58, ramT = 41, cpuL = 8, gpuL = 2;
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)Sampler.Capacity;              // 0..1 over 30 minutes
            bool gaming = t > 0.55 && t < 0.86, burst = t > 0.30 && t < 0.36;
            double cpuLoadTarget = gaming ? 46 + 10 * Math.Sin(i / 23.0) : burst ? 92 : 6 + 4 * Math.Sin(i / 61.0);
            double gpuLoadTarget = gaming ? 93 + 5 * Math.Sin(i / 17.0) : burst ? 4 : 2;
            cpuL += (cpuLoadTarget - cpuL) * 0.25 + rnd.NextDouble() * 6 - 3;
            gpuL += (gpuLoadTarget - gpuL) * 0.3 + rnd.NextDouble() * 4 - 2;
            cpuL = Clamp(cpuL, 0, 100); gpuL = Clamp(gpuL, 0, 100);
            double cpuTarget = (intel ? 38 : 44) + cpuL * (hot ? 0.58 : 0.40) + (hot ? 12 : 0);
            cpuT += (cpuTarget - cpuT) * 0.08 + rnd.NextDouble() * 1.4 - 0.7;
            gpuT += (36 + gpuL * 0.30 - gpuT) * 0.02;
            hotT += (gpuT + 4 + gpuL * 0.16 - hotT) * 0.05;
            vramT += (54 + gpuL * 0.24 - vramT) * 0.015;
            ramT += ((hot ? 50 : 39) + cpuL * 0.06 + gpuL * 0.05 - ramT) * 0.01;
            double ramUsed = 13.4 + (gaming ? 6.2 : 0) + 0.3 * Math.Sin(i / 90.0);

            var v = new Dictionary<string, float>
            {
                { "cpu.temp", (float)cpuT }, { "cpu.load", (float)cpuL }, { "cpu.power", (float)(18 + cpuL * 0.95) },
                { "ram.load", (float)(ramUsed / 31.1 * 100) }, { "ram.used", (float)ramUsed }, { "ram.total", 31.1f },
            };
            if (!noGpu)
            {
                v["gpu.temp"] = (float)gpuT; v["gpu.hot"] = (float)hotT; v["gpu.load"] = (float)gpuL;
                if (!intel) v["gpu.vram"] = (float)vramT; else v["gpu.vram"] = (float)(vramT + 6);
            }
            if (!ddr4)
            {
                v["ram.dimm0"] = (float)ramT; v["ram.dimm1"] = (float)(ramT - 1.5); v["ram.temp"] = (float)ramT; v["ram.limit"] = 55;
            }
            double caseDuty = Clamp(30 + (cpuT - 50) * 1.4, 25, 100), cpuDuty = Clamp(25 + (cpuT - 45) * 2.0, 20, 100);
            if (!noFans)
            {
                v["duty:c0"] = (float)Math.Round(cpuDuty); v["rpm:r0"] = (float)(cpuDuty * 15.5);
                v["duty:c1"] = (float)Math.Round(caseDuty); v["rpm:r1"] = (float)(caseDuty * 13.2);
                v["duty:c2"] = (float)Math.Round(caseDuty); v["rpm:r2"] = (float)(caseDuty * 14.1);
                v["duty:c3"] = (float)Math.Round(caseDuty * 0.8); v["rpm:r3"] = (float)(caseDuty * 0.8 * 13.9);
                v["duty:c4"] = noGpu ? 70 : (float)(gpuL > 40 ? 38 + gpuL * 0.2 : 0);
                if (noGpu) v["rpm:r4"] = 2780;
            }
            sampler.Feed(v, fans, dimms, names, gpuName, starting ? null : "Quiet");
        }
    }

    static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }
}
