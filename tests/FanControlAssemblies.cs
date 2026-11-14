using System;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
/// Loads Fan Control's own assemblies (FanControl.IPC, gRPC, protobuf and their dependencies) from the Fan Control
/// folder the tests were built against, like Fan Control has them next to the plugin at runtime. They are never copied
/// into the build output. Any version is accepted, which also stands in for Fan Control's binding redirects.
/// </summary>
static class FanControlAssemblies
{
    static bool _installed;

    public static string Dir
    {
        get
        {
            return typeof(FanControlAssemblies).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key == "FanControlDir").Select(a => a.Value).FirstOrDefault();
        }
    }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        string dir = Dir;
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            if (string.IsNullOrEmpty(dir)) return null;
            var file = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
    }
}
