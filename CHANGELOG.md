# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [0.1.0] - unreleased

First public version.

### Added
- Dashboard section at the top of Fan Control's Home page: CPU, GPU, RAM and fan cards; temperature and load history
  charts with limit lines, a synced hover crosshair and a clickable legend.
- **Customize mode** on the dashboard itself ("Customize" in the header): click a card or chart to edit it, drag it by its
  handle (or use the arrows) to reorder it, hide it with the eye, add new ones; Display options and Reset in the same bar.
  Saved to `%AppData%\FanControl.Dashboard\settings.json` (files of the first version are converted).
- **Cards for any value**: a main value, a warning limit (headroom, amber/red, dashed line in charts), an optional bar,
  details and a colour shared with the value's chart lines. CPU, GPU, RAM and Fans are ready-made cards built the same way.
- **Charts from any sensors** that share a unit (°C, %, rpm, W, MHz, V), each with its own axis; add, remove and reorder them.
- **Sensor picker** with search, grouped by hardware (CPU, GPU, memory, motherboard, storage, Fan Control's mix and custom
  sensors) and the live value of every sensor.
- Fans card: choose, rename and order fans (names change in the dashboard only).
- Display options: °C or °F, compact density, charts side by side or stacked, chart height, line smoothing, limit lines.
- History of one hour (2 / 5 / 10 / 30 / 60 minutes); charts draw at most about two points per pixel.
- Plugin sensors `CPU load %`, `GPU load %`, `RAM used %`.
- Works in both Fan Control V285 builds, .NET Framework 4.8 and .NET 10, with the same DLL. JSON is now read and written
  by the plugin itself instead of `System.Web.Extensions` (which .NET 10 does not have); the settings file format is unchanged.
- Sensor detection for Intel CPUs and NVIDIA GPUs (LibreHardwareMonitor naming) besides AMD CPUs and AMD GPUs (ADLX).
- The RAM card's warning can follow the modules' own reported threshold (DDR5 SPD hub).
- States for missing hardware: no GPU sensors; no module temperatures (DDR4 and many laptops: shows memory in use instead);
  no enabled fan controls; waiting for Fan Control; no new values from Fan Control. Legends list only series that have data.
- Light theme support with separate series and status colours; the view restyles when Fan Control's theme changes.
- Keyboard access (Tab, Enter/Space, arrow keys, Esc) and screen reader names for every control.
- The collapsed header shows CPU, GPU and RAM temperatures.
- Memory watchdog (logs Fan Control's memory every 30 minutes, stops drawing above 2 GB) and redrawing only while visible
  and in use, while the out-of-memory crash reports are investigated (see README, Known issues).
- `Install-Dashboard.ps1` / `Uninstall-Dashboard.ps1` find Fan Control, unblock the downloaded DLL and restart Fan Control.
- Harness with demo data and light/dark MaterialDesign hosting for screenshots; xUnit tests; CI and tag-based releases.
  CI runs the tests and the harness under both .NET Framework 4.8 and .NET 10 against the official Fan Control builds.

### Changed (compared with the private pre-release builds)
- Default CPU limit is 95 °C (was 89 °C, the Tjmax of Ryzen X3D CPUs); set it to your CPU's Tjmax.
- Out-of-range values typed into the limits are clamped to the allowed range instead of being reset to the default.
- Settings are written to a temporary file first and then swapped in, so a crash cannot leave a truncated file.
- The headroom text turns amber near the limit and red at or above it (it used to read "0 °C below" when over the limit).
- Fan Control's sensor list is re-read at most once a minute when sensors are missing (it was re-read every second
  when none matched).
