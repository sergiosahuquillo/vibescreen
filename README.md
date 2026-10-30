<div align="center">

<img src=".github/assets/logo.png" width="72" alt="VibeScreen logo" />

# VibeScreen

**Turn a USB smart screen into a live hardware dashboard.**

</div>

VibeScreen drives Turing Smart Screen–style USB-C displays: a background service paints your sensors on
the panel, and a desktop app lets you design the themes visually.

This is the **developer README**: how the repository is laid out, how to build, test and release it.
If you only want to use the app, the project page has screenshots, features and install instructions:
[.github/README.md](.github/README.md), the [website](https://sergiosahuquillo.github.io/vibescreen/),
or the [latest release](https://github.com/sergiosahuquillo/vibescreen/releases/latest).

**Status:** stable, version 1.0. Windows 10/11 x64 only for now. The sensor backend is Windows-only, and
the Linux one is planned. The supported screen is the Turing Smart Screen 3.5" (revision A,
USB ID `1A86:5722`).

## What's in the box

The installer (`VibeScreen-<version>-setup.exe`) sets up:

- The **window** in `Program Files\VibeScreen` and the **service** in its own `service` folder, with a
  Start menu shortcut (and a desktop one, if you tick it).
- The **Windows service** `VibeScreen`, which starts with the PC and restarts itself if it dies. The
  window does *not* start with Windows, on purpose: with a theme set the panel doesn't need it.
- **PawnIO**, the kernel driver that gives access to CPU and motherboard sensors, if it isn't there
  already. The installer explains it on its own page first, and never removes it on uninstall because
  other programs may be using it.
- The **default theme**, on the first install only, so the panel shows something right away.

Updating is running the new installer over the old one: themes and settings are kept. The window does
it for you from **Settings → Updates**: it checks the latest GitHub release when it opens and once a
day (or only when asked, or never — the user picks), and installing is always a click plus Windows'
administrator prompt. Uninstalling asks whether to delete themes and settings too (default: no).

The installer is **not code-signed** yet, so SmartScreen warns the first time: *More info* →
*Run anyway*.

## Repository layout

| Project | What it is | Depends on |
|---|---|---|
| `src/VibeScreen.Protocol` | Screen protocol, driver and scheduler. **Zero external dependencies.** | — |
| `src/VibeScreen.Contracts` | IPC DTOs and client, and the localized notice texts. **No dependencies.** | — |
| `src/VibeScreen.Core` | Serial transport, port detection, sensors, layouts and rendering | Protocol, Contracts, SkiaSharp, LibreHardwareMonitorLib (Windows only) |
| `src/VibeScreen.Service` | The process that holds the panel open | Core, Contracts |
| `src/VibeScreen.App` | The GUI: tray, settings, theme editor and sensor explorer | Core, Contracts, Avalonia |
| `tools/VibeScreen.Cli` | `vibescreen`: talks to the panel or to the service | Core, Contracts |
| `tools/VibeScreen.*Check` | Self-contained verifiers (see below) | — |
| `tools/VibeScreen.IconExport`, `BrandAssets` | Generate the icons and brand assets at packaging time | — |
| `packaging/` | Windows installer (Inno Setup), default theme, systemd unit | — |
| `design/` | The UI specification, checked on every build | — |

`Protocol` has no dependencies on purpose: all the protocol logic is verified without hardware and
without restoring a single package, against vectors generated with the reference Python implementation.

## Build and verify

You need the **.NET 10 SDK** (pinned in `global.json`) on Windows.

```bash
dotnet build
```

There is no unit-test framework, deliberately. Instead, each area has a small console verifier that
returns `0` or `1`, so they work as-is in CI. None of them needs hardware or a display:

```bash
dotnet run --project tools/VibeScreen.ProtocolCheck       # protocol, against the Python reference vectors
dotnet run --project tools/VibeScreen.CoreCheck           # identifiers, layouts and rendering
dotnet run --project tools/VibeScreen.ServiceCheck        # IPC and reconnection
dotnet run --project tools/VibeScreen.ThemeCheck          # the window against design/ and Fluent
dotnet run --project tools/VibeScreen.LocalizationCheck   # languages: keys, gaps and live switching
```

The build treats warnings as errors and enforces `.editorconfig`, so a clean `dotnet build` is already
half a review. CI runs the build and every verifier except `ThemeCheck` on each push (see
`.github/workflows/build.yml`), so run that one yourself after touching the UI.

Projects that target several frameworks must be built for `net10.0-windows` on Windows, because that is
where the registry (port enumeration by VID/PID) and the sensors live.

## Run from source

Run the service from an **administrator** console, since ring 0 access is what gives it the sensors,
and the app from a normal one. PawnIO must be installed (the installer does it; a release build is the
easiest way to get it).

```bash
dotnet run --project src/VibeScreen.Service -f net10.0-windows
dotnet run --project src/VibeScreen.App
```

While the service is running it owns the panel, so direct CLI commands will answer "port busy": go
through the service instead (`vibescreen service ...`).

### Command-line tool

Close any program that has the screen open first.

```bash
dotnet run --project tools/VibeScreen.Cli -f net10.0-windows -- <command>
```

```
vibescreen ports                  # serial ports with their VID/PID
vibescreen detect                 # compatible panels
vibescreen info                   # model, resolution, throughput
vibescreen pattern                # calibration chart drawn with Skia
vibescreen bench                  # real throughput, compared with the reference
vibescreen brightness 20
vibescreen off | on               # screen off / on
vibescreen clear
vibescreen sensors [filter]       # sensors with their canonical identifier
vibescreen run packaging/themes/VibeScreen/VibeScreen.json --seconds 30
vibescreen preview packaging/themes/VibeScreen/VibeScreen.json theme.png   # no panel needed
vibescreen service status | sensors | layout | reload | random | brightness | on | off
```

## How it works

- **The service owns the panel.** It keeps the connection open, paints the layout and survives the
  screen being unplugged (it retries at 1, 2, 5 and 10 s, re-detecting the port). The CLI and the GUI
  talk to it over a named pipe, one JSON message per line, using the DTOs in `VibeScreen.Contracts`.
- **The panel is a constant-throughput byte pipe** (about 256 KB/s on a Turing 3.5" rev. A), so
  rendering is planned against a byte budget, one blit per dirty rectangle, and the editor tells you how
  much of the link a theme needs.
- **The GUI doesn't read sensors.** It isn't elevated, so it asks the service over the pipe. Its preview
  is drawn by the very same `LayoutRenderer` the service uses.
- **A theme is a folder**, `layouts\<name>\<name>.json` plus its background image, under
  `%ProgramData%\VibeScreen`. It lists widgets (`text`, `clock`, `bar`, `gauge`, `chart`) that name
  their sensor by a **canonical identifier**, `{family}/{n}/{type}/{name}` (for example
  `cpu/0/temperature/package`), never by the backend's own name, so a theme made on an AMD machine works
  on an Intel one.
- **Sensors come from LibreHardwareMonitor**, which reaches the hardware through PawnIO.
- **The GUI has no XAML**: it is built in C#, so mistakes surface at compile time. It runs in the
  tray, there is one window per user, and its look is specified by `design/app.css`, verified by
  `ThemeCheck`.
- **Two languages**, English and Spanish, switchable live (`src/VibeScreen.App/Localization`).

The long version, with the reasons behind each decision, is in
[docs/design-notes.md](docs/design-notes.md). The UI specification is in
[design/README.md](design/README.md).

## Releases

Every push to `main` is a release: [`build.yml`](.github/workflows/build.yml) builds, runs the
verifiers, packages the installer and publishes "VibeScreen X.Y.Z" with the commits since the previous
one as the description. GitHub attaches the source from the tag.

**The version number is never edited by hand.** `VersionPrefix` in `Directory.Build.props` only
counts as MAJOR.MINOR; CI sets the patch to the next one after the latest `vMAJOR.MINOR.*` tag and
passes it to both executables and the installer, so the status bar, the executable, *Installed apps*,
the tag and the release all agree. To move to 1.1 or 2.0, change `VersionPrefix`; the next release
comes out as 1.1.0. Local builds carry a `-dev` suffix and other branches `-dev.<run>`, so they can't be
mistaken for a release. A commit with `[skip ci]` in its message doesn't produce one, and neither does
a push that only touches documentation (both READMEs, `.github/assets/` and `docs/`) or the
[website](#website).

On other branches and in pull requests the installer is still built and left as a workflow artifact, so
you can try it before merging. To build it locally you need Inno Setup 7
(`winget install --id JRSoftware.InnoSetup.7 -e`):

```powershell
./packaging/windows/build.ps1 -Version 1.0.0
```

Everything lands in `artifacts/`: the icon, both publishes, PawnIO and the installer. PawnIO is
downloaded and checked against a pinned SHA-256, never stored in the repository. Code signing is
still to do: the workflow has the spot marked for it.

## Website

[sergiosahuquillo.github.io/vibescreen](https://sergiosahuquillo.github.io/vibescreen/) is plain HTML,
CSS and JavaScript in `site/`, with no build step. [`pages.yml`](.github/workflows/pages.yml) publishes
it on every push to `main` that touches it; nothing is duplicated: the screenshots are copied from
`.github/assets/` to `shots/` and the window's fonts from `src/VibeScreen.App/Assets/Fonts` to `fonts/`
when publishing. Its colors are the window's, from `design/app.css`.

- **Texts** live in `site/i18n/en.json` and `site/i18n/es.json`. The HTML only names the key
  (`data-i18n`, `data-i18n-html` for texts with markup, `data-i18n-attr` for attributes) and `app.js`
  fills it in. The language comes from `?lang=`, the visitor's last choice or the browser.
- **Download button**: it points to the latest release and, through the GitHub API, straight to its
  installer with the version and size, so a new release shows up on its own.
- **Locally**: `node scripts/site/serve.mjs` serves it at http://localhost:4321 laid out exactly as
  published.
- **Check**: `node scripts/site/check-i18n.mjs` fails if a key is missing in one language, the
  `{{placeholders}}` differ, or a key is used without text or defined without use. CI runs it before
  publishing.

Changes to the website don't produce a release of the app.

## Credits

- The screen protocol comes from the reverse engineering in
  [mathoudebine/turing-smart-screen-python](https://github.com/mathoudebine/turing-smart-screen-python)
  (GPL-3.0). `tools/VibeScreen.ProtocolCheck/vectors.json` is generated with that project's logic and is
  compared against byte by byte.
- Windows sensors come from
  [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0), which
  reaches the hardware through [PawnIO](https://pawnio.eu/).
- [Avalonia](https://avaloniaui.net/) and [SkiaSharp](https://github.com/mono/SkiaSharp) for the window
  and the rendering.

## License

[GNU General Public License v3.0](LICENSE).
