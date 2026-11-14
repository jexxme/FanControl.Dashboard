# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Use GitHub's private reporting instead:
**Security → Report a vulnerability** on this repository.

Relevant areas include the install scripts (they run with administrator rights), the plugin running inside
Fan Control (which runs elevated), and how the plugin reads files (Fan Control's configuration, the settings file).

## What the plugin does

- Runs inside Fan Control's process, with Fan Control's rights.
- Reads sensor values over Fan Control's local IPC (a named pipe), Windows performance counters, and Fan Control's
  active configuration file. It makes no network connections.
- Writes only `%AppData%\FanControl.Dashboard\settings.json`, and log lines through Fan Control's plugin logger.
- Releases come with SHA-256 checksums (`SHA256SUMS.txt`).
