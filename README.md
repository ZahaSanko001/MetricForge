# MetricForge

<p align="center">
  <img src="docs/media/icon.png" width="96" alt="MetricForge icon">
</p>

<h3 align="center">A lightweight Windows taskbar monitor for CPU, RAM, and network activity.</h3>

<p align="center">
  <img src="https://img.shields.io/badge/platform-Windows-1A1A1A?style=for-the-badge&logo=windows&logoColor=fbbf24" alt="Windows">
  <img src="https://img.shields.io/badge/.NET-10-1A1A1A?style=for-the-badge&logo=dotnet&logoColor=fbbf24" alt=".NET 10">
  <img src="https://img.shields.io/badge/license-MIT-fbbf24?style=for-the-badge" alt="MIT License">
</p>

> Keep an eye on your system at a glance. MetricForge displays compact, color-coded CPU, memory, and network indicators just above the Windows taskbar.

## Preview

<!-- Add your screenshots to docs/screenshots/ using the filenames below. -->

<p align="center">
  <img src="docs/screenshots/taskbar-indicators.png" width="720" alt="MetricForge indicators preview">
</p>

<p align="center">
  <img src="docs/screenshots/settings.png" width="420" alt="MetricForge settings preview">
</p>

<p align="center">
  <img src="docs/screenshots/tray-menu.png" width="320" alt="MetricForge tray menu preview">
  <img src="docs/screenshots/demo.gif" width="480" alt="MetricForge demo">
</p>

## Highlights

- Three compact vertical indicators for CPU, RAM, and network activity.
- Transparent indicator backgrounds with amber `#fbbf24` borders.
- Green, yellow, and red usage colors for quick status recognition.
- Configurable update interval and indicator size.
- Adjustable bar opacity from the Settings window.
- Configurable network peak speed for accurate network scaling.
- Runs quietly from the Windows system tray.
- Click-through overlay that does not interfere with taskbar interaction.
- No administrator privileges required.

## Download and use MetricForge

You do not need to install an IDE, .NET, or any programming tools to use MetricForge.

### For users

1. Download the latest `MetricForge-win-x64.zip` file from the [Releases](https://github.com/ZahaSanko001/MetricForge/releases) page.
2. Right-click the ZIP file and choose **Extract All**.
3. Open the extracted folder and double-click `MetricForge.exe`.
4. MetricForge will start in the Windows system tray. Look for its icon near the clock.
5. Right-click the tray icon to pause the indicators, open **Settings...**, or exit the app.

Do not run the executable directly from inside the ZIP file. Extract the ZIP first.

On the first launch, Windows may show a SmartScreen warning because the application is not code-signed. If you downloaded it from the official project release, select **More info** and then **Run anyway**.

### For the person creating a release

The ready-to-distribute folder is:

```text
publish/MetricForge-win-x64/
```

ZIP the contents of this folder and upload the ZIP to a GitHub Release using a name such as:

```text
MetricForge-win-x64.zip
```

Users only need that ZIP file. They should not need the source-code repository.

## Requirements

- Windows 10 or later for the self-contained release.
- .NET 10 Desktop Runtime only when using the framework-dependent package.
- .NET 10 SDK only when building from source.

## Running from source

```powershell
git clone https://github.com/ZahaSanko001/MetricForge.git
cd MetricForge
dotnet run
```

The application starts in the system tray. Right-click the tray icon to pause/resume indicators, open Settings, or exit.

## Settings

Open **Settings...** from the tray menu to configure:

| Setting | Description |
| --- | --- |
| Bar size | Controls the width and height of each vertical indicator. |
| Bar opacity | Controls the translucency of the colored bars and borders. |
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

## Publishing a single-file application

Create a standalone Windows executable that users can run without an IDE, source code, or separate .NET installation:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish/MetricForge-win-x64
```

The resulting `publish/MetricForge-win-x64/MetricForge.exe` is the file to distribute. The application icon is embedded, so no separate resource folder is required.

Windows may display a SmartScreen warning for unsigned applications. Code-signing the executable is recommended for public distribution.

## Project structure

```text
Core/                    Application models, interfaces, and orchestration
Infrastructure/          Windows metrics collectors and taskbar overlay renderer
Presentation/            Tray application, settings UI, and icon resources
Program.cs               Dependency injection and application startup
```
## Contributing

Issues, suggestions, and pull requests are welcome. Please include your Windows version, .NET version, and reproduction steps when reporting a problem.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
