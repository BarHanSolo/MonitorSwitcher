# Monitor Switcher

**Switch monitor inputs on Windows without touching the monitor buttons.**

Monitor Switcher is a small, portable Windows tray application that uses **DDC/CI** to read and change monitor input sources.

It is useful for setups with:

* multiple monitors,
* multiple computers,
* monitors shared between computers,
* KVM-like setups where switching the mouse and keyboard is not enough,
* workstations with many displays,
* control rooms and other multi-display environments.

Instead of manually switching each monitor's input, you can define profiles such as:

```text
Work
Home
Computer 1
Computer 2
```

and switch the inputs of all configured monitors with a single click.

## Portable — no installation required

Monitor Switcher is distributed as a self-contained Windows executable.

Copy these two files to a computer:

```text
MonitorSwitcher.exe
monitors.json
```

and run the executable.

There is no installer, Windows service, driver, or .NET Runtime installation required.

The application does not require administrator privileges under normal Windows security policies.

> Corporate security policies such as AppLocker or application-control software may still prevent an executable from running.

## Multiple computers

Monitor Switcher can be used independently on multiple computers.

For example:

```text
Computer 1
├── MonitorSwitcher.exe
└── monitors.json

Computer 2
├── MonitorSwitcher.exe
└── monitors.json
```

Each computer controls the monitors that are physically connected to it and exposed through Windows/DDC/CI.

The same executable can be copied to both machines.

Each machine can use the same configuration or its own `monitors.json`, depending on the monitor setup.

## How it works

Monitor Switcher uses the **DDC/CI VCP `0x60` input source command** to read and change monitor inputs.

For example, a monitor might report:

```text
VCP 0x60 = 17 → DisplayPort
VCP 0x60 = 18 → HDMI
```

The exact values depend on the monitor.

The application identifies monitors using their Windows hardware IDs and maps those IDs to configured input values.

## Configuration

Create a file named `monitors.json` next to `MonitorSwitcher.exe`.

An example configuration is provided in `monitors.example.json`.

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

## Profiles

Clicking a profile switches all configured monitors to that profile.

For example:

```text
Work
Home
```

can switch an entire multi-monitor workstation from one computer to another.

Monitors that are already using the requested input are skipped.

## Individual monitor switching

Clicking an individual monitor in the tray menu cycles through all profiles configured for that monitor.

For example:

```text
Work → Home → Work
```

## Slow monitors

Some monitors are surprisingly slow when responding to DDC/CI commands.

Monitor Switcher retries operations for up to 5 seconds and verifies the resulting VCP `0x60` value after switching.

This is intentional and helps with monitors that take several seconds to change inputs.

## Requirements

* Windows
* Monitor with DDC/CI support
* DDC/CI enabled on the monitor
* A monitor input that can be controlled through VCP `0x60`

## Building

Requires the .NET 9 SDK.

Build:

```powershell
dotnet build
```

Publish a self-contained Windows x64 executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The published executable does not require the .NET Runtime to be installed.

## Starting with Windows

To start Monitor Switcher automatically:

1. Press `Win + R`.
2. Enter:

```text
shell:startup
```

3. Create a shortcut to `MonitorSwitcher.exe`.

The application starts directly in the system tray.

## Troubleshooting

### A monitor is not detected

Check that the configured `id` matches the monitor hardware ID reported by Windows.

Monitor Switcher displays diagnostic information when a configured monitor cannot be found.

### Input switching does not work

Check that:

* DDC/CI is enabled,
* the monitor supports input switching through DDC/CI,
* VCP `0x60` reports the expected values,
* the values in `monitors.json` are correct.

Some monitors may take several seconds to respond.

### The monitor switches but the application reports an error

The monitor may be responding slowly to DDC/CI.

Monitor Switcher retries operations for up to 5 seconds and verifies the resulting input value.
