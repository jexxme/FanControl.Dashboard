# Contributing

Thanks for helping! Bug reports, sensor lists from hardware the dashboard does not recognise yet, and pull requests are welcome.

## Reporting problems

- Use the issue templates. Always include your **Fan Control version** and which build you use
  (.NET Framework 4.8 or .NET 10; the download is named `net_4_8` or `net_10_0`).
- Attach the lines starting with `[Dashboard]` from Fan Control's `log.txt` (in Fan Control's folder).
- For "a card shows –" problems, attach the sensor list: build the solution and run
  `tools\FcTool\bin\Release\FcTool.exe sensors > sensors.txt` while Fan Control runs. It contains hardware names and IDs;
  remove anything you consider private.

## Development setup

1. Install Fan Control V285 and the [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer (10 to also run the
   tests and the harness under .NET 10, see README, Building).
2. `.\build.ps1` builds everything, runs the tests and copies the plugin to `dist\`.
   Fan Control in another folder: `.\build.ps1 -FanControlDir <folder>` or set `FANCONTROL_DIR`.
3. Look at your change without restarting Fan Control:
   `harness\bin\Release\Harness.exe --demo full --theme dark --out a.png` (see the README for scenarios and options).
   Check both themes and a narrow width (`--width 400`).
4. Try it in Fan Control: `.\dist\Install-Dashboard.ps1` (closes and restarts Fan Control).

Never commit Fan Control's DLLs or any other file from Fan Control: its license does not allow redistribution.
`.gitignore` blocks the usual names, and CI downloads the official release at build time.

## Guidelines

- **Never risk fan control.** Everything the plugin does inside Fan Control must be wrapped so that a failure only
  disables the dashboard. No blocking work on the UI thread: IPC and counters run on the sampler thread.
- **Keep the memory safeguards.** Changes to `Health` (the watchdog) or to when the view redraws
  (`DashboardView.Watched`) need a clear reason in the pull request, because of the open out-of-memory investigation.
- **UI consistency**: use the `Palette` colours and the controls in `Controls.cs` (`Pressable`, `Segmented`,
  `IconButton`, `TextButton`) so hover, focus, keyboard and screen reader behaviour stay the same everywhere.
  Text is short and in sentence case; units are written as `55 °C`, `40 %`, `1200 rpm`.
- **Pure logic gets tests** in `tests/`. The code stays at C# 7.3 so the Visual Studio Build Tools fallback keeps working.
- **Both Fan Control builds**: the plugin targets .NET Framework 4.8 but must also load under .NET 10. Use only APIs that
  exist in both (no `System.Web`, `System.Configuration`, WCF, remoting, ...) and no NuGet packages inside the plugin
  (Fan Control loads only the one DLL). CI catches mistakes: `FanControlBindingTests` JIT-compiles every method under .NET 10.
- Add a line to `CHANGELOG.md` under the next version.

## Releasing (maintainers)

1. Set `<Version>` in `Directory.Build.props` and give the `CHANGELOG.md` section its date.
2. Commit, then tag and push, e.g. `git tag v0.1.0` and `git push origin v0.1.0`.
3. The release workflow checks that tag, version and changelog agree, builds, tests, and publishes
   `FanControl.Dashboard.dll`, `FanControl.Dashboard-vX.Y.Z.zip` (DLL, install and uninstall scripts, README, LICENSE)
   and `SHA256SUMS.txt`.
