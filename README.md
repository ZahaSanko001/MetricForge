# TaskbarProgress

<p align="center">
  <img src="Presentation/Resources/Icons/icon.png" width="96" alt="TaskbarProgress icon">
</p>

<h3 align="center">A lightweight Windows taskbar monitor for CPU, RAM, and network activity.</h3>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows-1A1A1A?style=for-the-badge&logo=windows&logoColor=fbbf24" alt="Windows">
  <img src="https://img.shields.io/badge/.NET-10-1A1A1A?style=for-the-badge&logo=dotnet&logoColor=fbbf24" alt=".NET 10">
  <img src="https://img.shields.io/badge/license-MIT-fbbf24?style=for-the-badge" alt="MIT License">
</p>

> Keep an eye on your system at a glance. TaskbarProgress displays compact, color-coded CPU, memory, and network indicators just above the Windows taskbar.

## Preview

<!-- Replace these placeholder paths with your screenshots later. -->

<p align="center">
  <img src="docs/screenshots/taskbar-indicators.png" width="720" alt="TaskbarProgress indicators preview">
</p>

<p align="center">
  <img src="docs/screenshots/settings.png" width="420" alt="TaskbarProgress settings preview">
</p>

## Highlights

- Three compact vertical indicators for CPU, RAM, and network activity.
- Transparent indicator backgrounds with amber `#fbbf24` borders.
- Green, yellow, and red usage colors for quick status recognition.
- Configurable update interval and indicator thickness.
- Configurable network peak speed for accurate network scaling.
- Runs quietly from the Windows system tray.
- Click-through overlay that does not interfere with taskbar interaction.
- No administrator privileges required.

## Requirements

- Windows 10 or later.
- .NET 10 Desktop Runtime when using the framework-dependent package.
- .NET 10 SDK when building from source.

## Running from source

```powershell
git clone https://github.com/your-username/TaskbarProgress.git
cd TaskbarProgress
dotnet run
```

The application starts in the system tray. Right-click the tray icon to pause/resume indicators, open Settings, or exit.

## Settings

Open **Settings...** from the tray menu to configure:

| Setting | Description |
| --- | --- |
| Bar thickness | Controls the width of each vertical indicator. |
| Update interval | Controls how often system values are refreshed. |
| Network peak | The connection peak in Kbps used to scale the network indicator. |

For example:

```text
100 Mbps = 100000 Kbps
1 Gbps   = 1000000 Kbps
```

## Building

Build a normal Debug version:

```powershell
dotnet build
```

Build a Release version:

```powershell
dotnet build -c Release
```

## Publishing for distribution

For a self-contained single-folder Windows build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish/win-x64
```

The contents of `publish/win-x64` can be placed in a ZIP file and distributed to Windows users. A self-contained build includes the .NET runtime, so users do not need to install .NET separately.

For a smaller framework-dependent build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish/win-x64-framework-dependent
```

## Project structure

```text
Core/                    Application models, interfaces, and orchestration
Infrastructure/          Windows metrics collectors and taskbar overlay renderer
Presentation/            Tray application, settings UI, and icon resources
Program.cs               Dependency injection and application startup
```

## Screenshots and media

Add screenshots to:

```text
docs/screenshots/taskbar-indicators.png
docs/screenshots/settings.png
```

Optional project media can be added to:

```text
docs/media/
```

## Contributing

Issues, suggestions, and pull requests are welcome. Please include your Windows version, .NET version, and reproduction steps when reporting a problem.

## License

This project is licensed under the MIT License. Add a `LICENSE` file containing the full license text before publishing the repository.
