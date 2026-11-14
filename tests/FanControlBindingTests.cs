using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FanControl.Dashboard;
using FanControl.Plugins;
using Xunit;

/// <summary>
/// The plugin DLL is built once, for .NET Framework 4.8, and must load in both Fan Control builds. These tests run under
/// both runtimes (net48 and net10.0-windows test targets), each with the Fan Control assemblies of the matching official
/// release next to the test DLL, the way Fan Control loads the plugin.
/// </summary>
public sealed class FanControlBindingTests
{
    static readonly Assembly Plugin = LoadPlugin();

    static Assembly LoadPlugin()
    {
        FanControlAssemblies.Install();
        return typeof(DashboardPlugin).Assembly;
    }

    public FanControlBindingTests() { FanControlAssemblies.Install(); }

    [Fact]
    public void Fan_control_assemblies_come_from_the_expected_build()
    {
        Assert.False(string.IsNullOrEmpty(FanControlAssemblies.Dir));
        Assert.True(File.Exists(Path.Combine(FanControlAssemblies.Dir, "FanControl.IPC.dll")), FanControlAssemblies.Dir);
        var ipc = Assembly.Load(new AssemblyName("FanControl.IPC"));
        var tfm = ipc.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
#if NETFRAMEWORK
        Assert.StartsWith(".NETFramework", tfm.FrameworkName);
#else
        Assert.StartsWith(".NETCoreApp", tfm.FrameworkName);
#endif
    }

    /// <summary>
    /// JIT-compiles every method of the plugin. A reference to anything the runtime does not have (e.g. a .NET Framework-only
    /// API like System.Web.Extensions, or an IPC type or signature that differs between the Fan Control builds) fails here.
    /// </summary>
    [Fact]
    public void Every_method_binds_in_this_runtime()
    {
        var failures = new List<string>();
        foreach (var type in Plugin.GetTypes())
        {
            if (type.ContainsGenericParameters) continue;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (var m in methods)
            {
                if (m.IsAbstract || m.ContainsGenericParameters || m.GetMethodBody() == null) continue;
                try { RuntimeHelpers.PrepareMethod(m.MethodHandle); }
                catch (Exception ex) { failures.Add(type.FullName + "." + m.Name + ": " + ex.GetType().Name + " " + ex.Message); }
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Plugin_references_nothing_that_is_net_framework_only()
    {
        var refs = Plugin.GetReferencedAssemblies();
        var names = refs.Select(a => a.Name).ToList();
        Assert.DoesNotContain("System.Web.Extensions", names);
        Assert.DoesNotContain("System.Web", names);
        // every reference (framework, WPF, Fan Control) must resolve in this runtime, by the identity the plugin was built against
        foreach (var r in refs) Assert.NotNull(Assembly.Load(r));
    }

    /// <summary>Calls Fan Control's IPC client for real. Without a running Fan Control (CI) it must fail with a gRPC error, not a load error.</summary>
    [Fact]
    public void Ipc_client_works_in_this_runtime()
    {
        object client = null;
        try
        {
            var sensors = Ipc.All(ref client);
            Assert.NotNull(sensors); // Fan Control is running on this machine
        }
        catch (Exception ex) when (ex.GetType().FullName == "Grpc.Core.RpcException")
        {
            // expected without Fan Control: the client was created and the call reached the transport
        }
        Assert.NotNull(client);
    }

    [Fact]
    public void Plugin_lifecycle_like_fan_control()
    {
        var log = new Logger();
        var container = new Container();
        var plugin = new DashboardPlugin(log);
        Assert.Equal("Dashboard", plugin.Name);
        plugin.Initialize();
        plugin.Load(container);
        plugin.Update();
        plugin.Close();

        Assert.Equal(new[] { "Dashboard/cpu-load", "Dashboard/gpu-load", "Dashboard/ram-load" }, container.TempSensors.Select(s => s.Id).ToArray());
        Assert.Contains(log.Lines, l => l.StartsWith("[Dashboard] initialized", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("initialize failed") || l.Contains("TypeLoad") || l.Contains("MissingMethod") || l.Contains("FileNotFound"));
    }

    sealed class Logger : IPluginLogger
    {
        public readonly List<string> Lines = new List<string>();
        public void Log(string message) { lock (Lines) Lines.Add(message); }
    }

    sealed class Container : IPluginSensorsContainer
    {
        readonly List<IPluginControlSensor> _controls = new List<IPluginControlSensor>();
        readonly List<IPluginSensor> _fans = new List<IPluginSensor>(), _temps = new List<IPluginSensor>();
        public List<IPluginControlSensor> ControlSensors { get { return _controls; } }
        public List<IPluginSensor> FanSensors { get { return _fans; } }
        public List<IPluginSensor> TempSensors { get { return _temps; } }
    }
}
