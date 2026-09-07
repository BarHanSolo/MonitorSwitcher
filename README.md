# Monitor Switcher

A small Windows tray application for switching monitor input sources using DDC/CI.

It allows you to:

- switch all configured monitors to a named profile,
- switch individual monitors between their configured profiles,
- cycle through all profiles configured for a monitor,
- detect monitors using their Windows hardware ID,
- verify that the requested input source was actually applied,
- retry slow or temporarily unresponsive monitors for up to 5 seconds.

The application runs in the Windows system tray.

## How it works

Monitor Switcher uses DDC/CI VCP command `0x60` to read and change the monitor input source.

Each monitor is identified by its hardware ID, for example:

```text
ACR06E5
AOCB327
```

The input source values are configured manually in `monitors.json`.

## Configuration

Create a file named `monitors.json` next to `MonitorSwitcher.exe`.

Example:

```json
{
  "monitors": [
    {
      "name": "Left",
      "id": "MONITOR_ID_1",
      "profiles": {
        "Work": 18,
        "Home": 17
      }
    },
    {
      "name": "Right",
      "id": "MONITOR_ID_2",
      "profiles": {
        "Work": 17,
        "Home": 15
      }
    }
  ]
}
```

### Monitor ID

The `id` field should contain the monitor hardware ID reported by Windows.

For example:

```text
ACR06E5
AOCB327
```

The application matches this ID against the monitor information returned by Windows.

### Input source values

The values in `profiles` are DDC/CI VCP `0x60` values.

For example:

```json
"profiles": {
  "Work": 17,
  "Home": 15
}
```

The actual values depend on the monitor and its inputs.

You can use a tool such as ControlMyMonitor to find the VCP `0x60` value for each input.

## Tray menu

The tray menu contains three sections:

```text
Work
Home
----------------
Left
Right
----------------
Exit
```

### Profiles

Clicking a profile switches all configured monitors to that profile.

A monitor that is already using the requested input is skipped.

### Individual monitors

Clicking a monitor cycles it to the next configured profile.

For example:

```text
Work → Home → Work
```

If a monitor has more profiles, all of them are included in the cycle.

## Slow monitors

Some monitors can take several seconds to switch inputs or respond to DDC/CI commands.

Monitor Switcher therefore retries DDC/CI operations for up to 5 seconds and verifies the resulting VCP `0x60` value after switching.

This is intentional.

## Requirements

- Windows
- A monitor with DDC/CI support
- DDC/CI enabled in the monitor's OSD/settings
- .NET 9 SDK for building the application

The published self-contained executable does not require the .NET runtime to be installed.

## Building

Clone the repository and run:

```powershell
dotnet build
```

To create a self-contained single-file executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable will be created under:

```text
bin\Release\net9.0-windows\win-x64\publish\
```

Copy `MonitorSwitcher.exe` and your `monitors.json` to the same directory.

## Starting with Windows

To start Monitor Switcher automatically with Windows:

1. Press `Win + R`.
2. Enter:

```text
shell:startup
```

3. Create a shortcut to `MonitorSwitcher.exe`.

The application will start minimized to the system tray.

## Troubleshooting

### A monitor is not detected

Check that the configured `id` matches the monitor hardware ID reported by Windows.

If the application cannot find a configured monitor, it displays diagnostic information containing the detected device IDs.

### Input switching does not work

Check:

- DDC/CI is enabled on the monitor.
- The monitor supports changing inputs through DDC/CI.
- VCP `0x60` contains the expected values.
- The configured values in `monitors.json` are correct.

Some monitors may take several seconds to respond after an input change.

### The monitor switches but the application reports an error

The monitor may be responding too slowly for DDC/CI.

Monitor Switcher retries operations for up to 5 seconds and verifies the resulting value. If the monitor takes longer than that, increase the retry timeout in `Program.cs`.

## License

This project is provided as-is for personal and hobby use.
