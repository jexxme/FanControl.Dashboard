using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

// Developer helper for Fan Control's IPC.
//   FcTool sensors [filter]   list every sensor with id, type, value and name
//   FcTool list               list the available configurations
//   FcTool load <path>        switch Fan Control to another configuration
//   FcTool show               bring the Fan Control window to the front
// Fan Control's assemblies are loaded from FANCONTROL_DIR or C:\Program Files (x86)\FanControl.
static class FcTool
{
    static readonly string App = Environment.GetEnvironmentVariable("FANCONTROL_DIR") ?? @"C:\Program Files (x86)\FanControl";

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

    static DateTime Deadline(int seconds) { return DateTime.UtcNow.AddSeconds(seconds); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string[] a)
    {
        string cmd = a.Length > 0 ? a[0] : "list";
        if (cmd == "sensors")
        {
            var sc = FanControl.IPC.IPCFactory.GetSensorClient();
            var r = sc.GetAllSensors(new GetAllSensorsRequest(), null, Deadline(5), CancellationToken.None);
            string filter = a.Length > 1 ? a[1] : null;
            foreach (var x in r.Sensors)
            {
                if (filter != null && x.Identifier.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && x.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.WriteLine(x.Identifier + "\t" + x.Type + "\t" + (x.HasValue ? x.Value.ToString("0.##", CultureInfo.InvariantCulture) : "null") + "\t" + x.Name);
            }
            return 0;
        }

        var fc = FanControl.IPC.IPCFactory.GetFanControlClient();
        if (cmd == "show") { Console.WriteLine(fc.ShowWindow(new ShowWindowRequest(), null, Deadline(5), CancellationToken.None)); return 0; }
        if (cmd == "load")
        {
            if (a.Length < 2) { Console.Error.WriteLine("usage: FcTool load <path to config .json>"); return 2; }
            Console.WriteLine(fc.LoadConfig(new LoadConfigRequest { Path = a[1] }, null, Deadline(15), CancellationToken.None));
        }
        if (cmd == "list" || cmd == "load")
        {
            var s = fc.ListAvailableConfigs(new ListAvailableConfigsRequest(), null, Deadline(5), CancellationToken.None);
            Console.WriteLine("current=" + s.CurrentConfig);
            Console.WriteLine("folder=" + s.ConfigFolder);
            foreach (var c in s.Configs) Console.WriteLine("  " + c);
            return 0;
        }
        Console.WriteLine("usage: FcTool sensors [filter] | list | load <path> | show");
        return 2;
    }
}
