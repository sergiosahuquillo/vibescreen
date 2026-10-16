# Design notes

Technical documentation for VibeScreen: how it is built and, above all, **why**. The
[README](../README.md) has the overview; this is the fine print. Read it before touching code: almost
every section comes from a failure that has already happened.

## Contents

- [The verifiers](#the-verifiers)
- [Windows packaging](#windows-packaging)
- [The service](#the-service)
- [The GUI](#the-gui)
- [Sensors and layouts](#sensors-and-layouts)
- [Decisions worth knowing before touching code](#decisions-worth-knowing-before-touching-code)

## The verifiers

`LocalizationCheck` makes sure Spanish and English have the same keys with the same gaps, that every
key the code uses exists and none is left over, and then mounts the whole window headless and switches
its language three times with all four sections mounted, looking for any trace of the previous
language in what is visible. If a service is running it talks to it and has more to look at; without
one, it looks at the "Service stopped" window.

`ThemeCheck` is the only one that compares the window against something that isn't code:
`design/app.css`, which is the export of the redesign and acts as the specification. It mounts the
real controls with headless Avalonia and asks them what they paint, instead of reading the styles and
assuming they apply — which is exactly what fails: a selector that doesn't match compiles just as
well as one that does. On top of that it walks Fluent's key table looking for states nobody has
dressed, which are the ones that would come out in Fluent's color in the middle of the palette. More
in `design/README.md`.

## Windows packaging

**The window and the service each go in their own folder**, and not out of taste: the window is
`net10.0` and the service `net10.0-windows`, so each one ships a different `VibeScreen.Core.dll` with
the same name — one with the sensors and the registry, the other without them — and in a shared folder
the last one copied would win without warning. Publishing as a single file would also avoid it, but it
changes how assemblies are loaded compared with what is used during development, and it doesn't save
the second runtime either.

**PawnIO is downloaded and verified**, not stored in the repository: `build.ps1` fetches the official
installer of a pinned version and compares it with the SHA-256 that GitHub publishes before putting it
inside.

**The file icon is the factory red.** While running, the window and the tray draw their icon with the
chosen accent; the executable's, the shortcut's and the installer's can't follow it, so they carry the
dark-mode red. It comes from the same code as the window's icon — `VibeScreen.IconExport` generates it
at packaging time — and not from a binary in the repository, which would fall behind at the first
change to the brand.

**And the installed taskbar icon stays red too.** The window gets the new icon when the accent
changes, but when there is a Start menu shortcut pointing at the executable, Windows treats the window
as that application and paints the shortcut's icon, without looking at the window's. Checked with the
three executables: the development one and the one in `artifacts/` follow the accent because no
shortcut points to them; the installed one doesn't, even when launched by hand. **It is left like this
on purpose.** The only thing that fixes it is giving the window its own AppUserModelID, and then
pinning the app from the Start menu duplicates it on the taskbar: the pin, which is a copy of the
shortcut, and next to it the open window, because Windows no longer recognizes them as the same
application. A color that doesn't match is better than two buttons.

## The service

The service is what holds the panel open: it keeps the connection, paints the layout and survives the
screen being unplugged (it retries at 1, 2, 5 and 10 s until it comes back, re-detecting the port,
because a `RESET` changes it). The CLI and the GUI talk to it over a named pipe, one JSON message per
line, with the DTOs of `VibeScreen.Contracts`. On Linux, .NET implements that pipe on top of a Unix
domain socket, so the transport is the same code on both systems.

To develop, in a console:

```
dotnet run --project src/VibeScreen.Service -f net10.0-windows
```

With the service running the panel is busy, so direct CLI commands will answer "port busy": everything
goes through `vibescreen service ...`.

**Windows.** It runs as LocalSystem, which is what gives it the ring 0 access the sensors need, and
keeps its configuration in `%ProgramData%\VibeScreen\config.json`. Without that file — the first time
after installing — it starts with the default theme if it's there; once the file is written, whatever
it says rules. The installer registers it; to do it by hand:

```
dotnet publish src/VibeScreen.Service -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
sc.exe create VibeScreen binPath= "<path>\vibescreen-service.exe" start= auto
sc.exe start VibeScreen
```

It is removed with `sc.exe stop VibeScreen` and `sc.exe delete VibeScreen`.

The pipe carries an explicit access control list — SYSTEM and administrators with full control, users
with read and write — because the GUI isn't elevated and, with the list Windows applies by default,
it couldn't even open the pipe of a service that runs as SYSTEM. The first instance reserves the name:
if another process already holds it, the service fails to start instead of letting someone
impersonate it.

**Linux.** It is a **user** unit, not a system one: Linux sensors come from files that need no
privileges, and this way the socket stays in the user's session. The configuration goes to
`~/.config/VibeScreen/config.json`. (Linux support is not released yet: there is no sensor backend
for it, and this unit hasn't been tested on a real machine.)

```
cp packaging/systemd/vibescreen.service ~/.config/systemd/user/
systemctl --user enable --now vibescreen
```

## The GUI

```bash
dotnet run --project src/VibeScreen.App
```

Avalonia 12, and **no XAML**: the interface is built in C#. The properties panel changes entirely
depending on the selected widget, which is exactly what XAML makes expensive, and this way every
mistake shows up at compile time instead of halfway through a run.

The window lives in the tray: closing it doesn't end the process, because the user expects the panel
to keep working and the window to come back with one click. On Windows 11 the icon appears in the
tray overflow until it is pinned, which is what the system does with any new icon.

**And there is only one per user.** Opening it again — the shortcut, with it hidden in the tray —
brings the existing one to the front instead of starting a second one with another icon and the same
settings. A mutex decides it and the notice goes over a named pipe, which is the same code on Linux.
The only time one is started on top of another is when changing the render mode, which restarts the
window: the new process is born before the old one leaves, so it carries `--restart` and waits to be
alone instead of waking the one that is closing.

Four sections, in a column on the left:

- **Panel.** What the service is doing, the brightness, the power state, which theme it is painting and
  how much link is being used right now.
- **Editor.** The preview with a pixel grid, dragging and stretching widgets, and the properties of
  each one: sensor, unit, decimals, which side the unit goes on, system font, color, and what is
  specific to each chart. Clocks are added without choosing a sensor, and come with the usual formats
  already made, with an example of how they will look. An arc can be set to "Write the value inside",
  and then its fields appear below: it doesn't ask for the sensor again, since it is the arc's. Anything
  that looks at a sensor also carries "Color by value", with the two cutoffs and the three colors, the
  range of each band written next to it — "under 40 °C", "40 to 80 °C", "80 °C or more" — and a button
  that sets green, amber and red spread across the range to start from. A widget is **duplicated** with
  `Ctrl+D`, or copied and pasted with `Ctrl+C` and `Ctrl+V`, or with the right button: it comes out
  with all its styles and a free identifier, and the only thing left to change is the sensor. The
  panel background is chosen there: a color, or an image with its fit. Below that, what the theme will
  ask of the panel. And the **Delete** button, set apart from the others, deletes the open theme.
- **Sensors.** The 328 sensors of the reference machine with their canonical identifier, their native
  name and their value, updating live. They are filtered by device and by text at the same time.
- **Settings.** The accent color, the font, the language and what the window is painted with. They go
  to the user's profile, not to the service's configuration: they belong to whoever looks at the window
  and not to the panel.

**Two languages, Spanish and English, switching live.** No library: the texts are in
`src/VibeScreen.App/Localization/es.json` and `en.json`, with named keys — `settings.title` — and a
plural is two keys, `.one` and `.other`. Each text is bound to a single key in the application resources,
which change with the language, just like the data font, so changing it repaints everything at once,
including what is in a hidden section, without rebuilding the views: the editor doesn't lose what hasn't
been saved. The numbers in the window follow the language — "1,234" or "1.234"; those of the panel and
its preview don't. By default, the system language.

**What the service and Core report travels as a code, not as a sentence.** A warning is a `Notice` —
`panel.not-found`, with its data — and whoever shows it words it: the window in its language, and the
CLI and the logs in Spanish. Their texts live next to the contract, in
`src/VibeScreen.Contracts/Notices`, because all three need them. What isn't ours — the message of a
system exception, one from System.Text.Json — travels as is, as data.

**Almost black — or almost white — and a single accent color.** The accent doesn't decorate: it marks
the title of each card, the active section, the selected row and the focus, and it is the only thing
the user chooses, in Settings. Changing it repaints the whole window without restarting, because the
brushes are shared and the Fluent theme resources are touched too, which is where the selection of a
list, the focus ring and the slider thumb come from. The green and amber of the warnings stay out of
that choice: "all good" and "careful" can't be the same color as everything else.

**And there are two palettes, not one.** The light one is measured separately and not lightened from the
dark one: the same role for each color and contrast checked on a white background. That is why **an
accent is a pair of tones and not a color**: the one that reads on near-black doesn't read on white.
There are eight to choose from, and what is saved is which of the eight — "red", "blue" — not its
`#RRGGBB`; saving the resolved color would leave the dark-background one on a white background when
switching mode. Files from before carry the hex and are translated when read. The mode is chosen in
Settings — dark, light or the system's, and the latter is followed live — and there is a shortcut in
the title bar.

**What is painted on the panel doesn't follow the window.** The screen's theme belongs to the service:
setting the window to light doesn't change a single pixel of what is being shown on the device. That is
why the editor's grid is white in both palettes, because it is drawn on top of the theme and not on top
of the window.

**Three fonts, and only one can be chosen.** Archivo writes what is read — labels, buttons, help —,
Space Grotesk the titles, and the monospaced one is kept for what is **data**: figures, units and
sensor identifiers, where the columns have to line up and a 0 can't be mistaken for an O. All three are
embedded in the executable, in `src/VibeScreen.App/Assets/Fonts`, with their OFL license next to
them: the window can't depend on them being installed.

They are static instances and not the variable fonts from Google Fonts, which is what you need to know
before touching them: Avalonia 12 doesn't expose the axes of a variable font, so with the variable
`.ttf` a weight of 600 would come out as a fake bold of 400.

**The monospaced one is the exception: it can be swapped for another one on the machine**, because it
is the only typographic choice the user has and some people have their own. JetBrains Mono is the
factory one and the first in the list, so the choice **can't fall back to squares**: whatever is
installed, there is always one that surely exists. Behind it go the well-known ones the machine has,
which `FontChoice` decides — pure, it receives the list of installed fonts, and it is checked without
depending on the fonts of the machine where the tests run. The list is requested from Skia, which is
what ends up painting. In Settings the dropdown is marked with the one actually being used, not the one
that was requested.

**Going back to factory settings also reaches the render**, which can't be changed live: instead of
lying by saying it is already on automatic, it leaves it requested for the next start, with the same
automatic way back as any other mode change.

**A widget's properties go in collapsible sections.** An arc with the value inside has thirty rows and
the column fits eleven. Split into Box, Sensor, the chart type and Value inside, only the type's is
open, which is what you come for; the closed ones summarize their values next to the title, so they can
be seen without opening them. Narrow fields go two at a time — X and Y, width and height, minimum and
maximum — and in those rows the counters lose their arrows, which left no room for the number. The
add dropdown closes by itself as soon as something is selected.

**The editor warns before it's too late.** Next to the theme, a notice says whether there are unsaved
changes, and the new-theme button and switching layout in the dropdown ask before taking them away. If
resizing the panel pushes any widget out, it says so — the loader would reject it on save, and the
preview doesn't give it away because the renderer clips against the canvas — but **nothing is moved
automatically**: the size fields also apply what is typed halfway, so typing "320" goes through a
16 px panel first and would leave the widgets crushed with no way back. Fitting them is a button
whoever wants it presses.

**And the hidden window doesn't work.** Closing it sends it to the tray by canceling the close, so
`OnClosed` never arrives and the timer stayed alive: every second it repainted with Skia a preview
nobody sees and made the service re-read sensors. Now the polling hooks into visibility.

**And in the whole column only one dropdown is open at a time.** Opening one closes the others — the
screen, the widget add menu and the properties sections count as one — because together they don't fit
and what had just been opened ended up below the edge. `Accordion` does it, listening at the root for
the dropdown's own event instead of keeping a list: the sections are rebuilt with every selected
widget, and a list would have to be unsubscribed by hand.

**The preview doesn't look like the panel: it is the panel.** It is painted by the same
`LayoutRenderer` the service uses, into the same bitmap and with the same pixels; Skia writes directly
into Avalonia's buffer. The grid and the selection handles are drawn on top, never inside the bitmap.

**A widget is copied by a serializer round trip**, not with `with`. `with` copies on the surface, and a
text or a chart carries its color rule and an arc carries the number inside: the copy would keep them
**shared** with the original, so changing the duplicate's color would change the first one's too. And a
`switch` by type would have to be remembered the day a widget is added. This way the copy is deep by
construction. The clipboard lives in the editor and not in the system's: copying from one theme and
pasting into another works, closing the window empties it.

**Widget shortcuts live on the canvas**, not on the window. `Del`, `Ctrl+C`, `Ctrl+V` and `Ctrl+D` are
handled by `PanelCanvas`, which only has focus when it has been clicked: this way `Ctrl+C` inside a
properties field still copies text and `Del` doesn't take a widget away while you are typing.

**Deleting a theme is deleting its folder**, image included, and there is no way back. The only thing
between the name and a recursive delete is `LayoutPaths.NameProblem`, so it is checked again on
deletion and not only on typing. If the deleted one was on the panel, the first remaining one is sent
to it with `chosen: false`: recovering is not choosing, and the draw stays as it was. And deleting the
**last** one leaves a loose end the editor can't tie: there is no IPC command to say "no theme", so the
panel keeps showing it from memory until another is applied and, if the service restarts, it goes
black with a warning. The message says so instead of hiding it.

**The GUI doesn't read sensors.** It isn't elevated — ring 0 is the service's business — so it asks for
the values over the pipe. For the preview it asks only for the open theme's sensors: refreshing
everything includes disks and network, and at twenty times a minute it shows on the panel.

**And whoever paints rules over the reading.** The sensors the panel is painting are not sampled again
to answer the window: what the panel read is served, which is what is on the screen. It isn't an
optimization, although it removes work. Some sensors **don't have a current value but a "how much has
passed since you asked"** — network and disk speeds, effective clocks — and those measure the gap
between one update and the next. With two observers sampling on their own, each takes a different
piece of the same second: the panel averaged whatever the window's polling left it, so **having the
editor open made what the screen measures worse**. Now the gap is the widget's `refreshMs`, which is
what it claims to be, and raising it really does smooth. The explorer is the deliberate exception: its
job is to say what the sensor reads now, so it refreshes on its own, and that is why it can show a
different number from the preview's.

What this does **not** fix: the preview and the panel always writing the same number. Each one
quantizes with its own stopwatch and the polling runs at 1 Hz, so the preview shows what the panel
painted with up to a couple of seconds of delay. With a sensor that jumps — the Ryzen's vcore can only
be 1.175, 1.18125 or 1.1875, and at two decimals that is 1.17, 1.18 and 1.19 — two different numbers
will still show up at times. The difference is that now it is **the same measurement, delayed** and
not two different measurements.

**If the window flickers**, the suspect is the Windows compositor with certain video drivers. What it
is painted with is chosen in Settings: automatic, Direct3D through ANGLE, native OpenGL — WGL on
Windows, GLX on Linux —, EGL (Linux only, because Windows' EGL is ANGLE's), Vulkan, or software, which
runs on the CPU and doesn't depend on the graphics card.

**Automatic is a list, not a choice**: Avalonia goes through it in order and keeps the first one it
manages to initialize. They are `AngleEgl, Software` on Windows and `Glx, Software` on X11, so
**WGL and Vulkan are only reached by asking for them**: they are the two GPU paths automatic never
tries, and the first candidates when the window flickers but is visible — that means ANGLE has
initialized fine and automatic stopped there.

Settings says which one is really being used. Avalonia 12 doesn't report it through any public route —
`AvaloniaLocator` no longer exists and `IPlatformGraphics` isn't public — so we look at which native
libraries the process has loaded: `av_libglesv2` is the ANGLE that ships with Avalonia and nobody else
loads it, `vulkan-1` is the Vulkan loader, `opengl32` only shows up with WGL, and none of the three
means it is painting on the CPU.

The mode **can't be changed live**: it is fixed when the application is built, before any window
exists, so changing it restarts the GUI. In return, choosing wrong doesn't lock anyone in, and without
needing any countdown — which wouldn't work either, because a black screen has nowhere to put it and a
crashed process has nobody to count it:

1. Choosing a mode writes it down as "to try" in `app.json`.
2. The next start **deletes it before using it** and starts with it.
3. If the window is visible, the user confirms it in a bar at the very top and it becomes the good one.
4. If it isn't visible — black, hung or crashed — the next start no longer finds the note and uses the
   last one that worked. There is no need to detect the failure: it is enough that this start doesn't
   get to confirm.

A failure so big that the application doesn't even open the window leaves it on software for the next
one. And above everything is `VIBESCREEN_RENDER`, which accepts `auto`, `direct3d` (or `angle`, `gpu`),
`opengl` (or `wgl`, `glx`), `egl`, `vulkan` and `software` (or `cpu`): it is what remains when the
window can't even be opened to change it, and while it is set the Settings dropdown shows up disabled
saying why.

**The service doesn't need to be there to open it.** Without the service the window keeps saying what
is missing, the editor keeps working with blank values, and as soon as the service comes back it
reconnects by itself.

## Sensors and layouts

A layout is a JSON with the resolution, the orientation, a background and a list of widgets. Each
widget names its sensor by the **canonical identifier**, `{family}/{n}/{type}/{name}`:

```
cpu/0/temperature/package     gpu/0/temperature/core     memory/0/data/used
cpu/0/load/total              gpu/0/load/core            motherboard/0/fan/cpu-fan
```

Layouts can't use the backend's identifiers, or a theme made on an AMD machine would fail on an Intel
one. The usual sensors receive a neutral name and the rest is derived from the backend's name.
`vibescreen sensors` lists the machine's with their native name next to them.

There are five widget types, and all carry `rect` as `[x, y, width, height]`, `refreshMs` and
`priority`:

| `type` | What it is | Its own properties |
|---|---|---|
| `text` | Fixed text, or a sensor's value | `decimals`, `unit`, `showUnit`, `unitPlacement`, `unitSpace`, `prefix`, `suffix`, `levels` |
| `clock` | The time, the date, or both | `format` |
| `bar` | Level bar | `min`, `max`, `direction`, `radius` |
| `gauge` | Needle arc; at 360 degrees, a ring | `min`, `max`, `thickness`, `startAngle`, `sweepAngle`, `round`, `label` |
| `chart` | Scrolling history, one sample per refresh | `min`, `max`, `samples`, `thickness`, `fill` |

`text` and `clock` share the font: `font`, `size`, `bold`, `italic`, `color` and `align`. The three
charts share `sensor`, `unit`, `color`, `background` — the color of the empty part —, `outline` with
its `outlineWidth` and, like `text` with a sensor, `levels`.

**The value is composed in parts, not with a format string.** A `text` with a sensor writes `prefix` +
number + unit + `suffix`, and the unit can go before or after, attached or separated. It is what lets
the GUI edit it with fields instead of asking the user to type `{0:F1} °C` by hand.

**Color can say something.** With `levels`, the widget is painted one color or another depending on
what its sensor reads, which is what turns a panel you look at into one you consult: if the CPU is red
you see it without reading the number. The three charts and `text` **with a sensor** carry it; fixed
text and the clock don't, because they have no value to look at.

```json
"levels": { "low": "#4CAF50", "medium": "#FFC107", "high": "#F44336", "mediumFrom": 40, "highFrom": 80 }
```

There are **always three bands**, and the five values are set by the user: 40 and 80 degrees may be
fine for a CPU and not for a disk, so VibeScreen ships no thresholds of its own — the only thing it
sets is the starting point of the editor's button, which is there to be adjusted. Below `mediumFrom`,
`low` rules; from there, inclusive, `medium`; and from `highFrom`, `high`. **There are no gaps by
construction and not by checking**: no ends are declared, only the two borders, so no value is left
without a color and there are no overlaps to resolve. The cutoffs are in the widget's unit, just like
`min` and `max`.

The bands are called by their **position** and not by what they mean, because there are sensors where
the worrying thing is the low end — free memory, battery, a fan's revolutions: this way, inverting the
criterion is choosing the colors the other way around and the file says nothing that isn't true.

**What it tints and what it doesn't.** What draws the value: the text's ink, the bar's fill, the arc,
the history line, its `fill` — toned down, or the line would vanish inside it — and the `outline`. Not
the `background`, which is the gap and not the value: in the same color as the fill you wouldn't see how
far it reaches. And the rule **recolors what already exists, it doesn't create it**: a widget without
an outline stays without an outline. The number inside an arc already inherits the arc's color when it
has none of its own, so it inherits the band too.

**Without a reading, the fixed colors are painted.** A sensor this machine doesn't have, or that hasn't
yielded a value yet, leaves the widget in its usual `color`. This way that color doesn't go dead when
the rule is switched on: it comes to mean "no data", which is distinguished from a real low value.

The color comes from the **value** and not from the drawing, so a cutoff above `max` does change the
color — the bar is already full when it does, and the editor warns about it. The history recolors **the
whole line** with the band of the last sample, not band by band: a bar at 85 isn't red only at the top
either. And a widget is sent to the panel also when **only** its color changes, which isn't a rare
case: with zero decimals and a cutoff at 40, a 39.6 and a 40.4 both write "40 °C". The link meter
doesn't change, because it already charges the worst case: the whole box on every refresh.

A theme with `levels` **can't be opened by an earlier version** of VibeScreen: the format rejects
properties it doesn't know instead of ignoring them. The `schema` is still 2 because raising it would
leave every already-saved theme unreadable, and a theme without a rule is saved byte for byte as
before.

**Charts can carry an outline.** With `outline` they get a border in whatever color you want, and
`outlineWidth` says how many pixels. It goes **inward from the box**, because the box is what is sent
to the panel and a stroke straddling the edge would be half lost when clipping. On the arc it isn't a
rectangle: the silhouette of the band is drawn, so the outline surrounds it instead of filling it, and
that is why an arc with an outline needs a bit more box than one without.

**The arc can carry the value written inside.** With `label` the number is put in the gap, centered on
the circumference without having to eyeball it:

```json
{ "type": "gauge", "id": "cpu", "rect": [20, 32, 130, 130], "sensor": "cpu/0/load/total",
  "min": 0, "max": 100, "thickness": 12, "startAngle": 270, "sweepAngle": 360,
  "label": { "size": 38, "bold": true, "decimals": 0 } }
```

The `label` has no sensor or unit of its own: they are the arc's, so the number and the needle can't
say different things. It carries the font (`font`, `size`, `bold`, `italic`), the `color` — without it,
the arc's — and the same composition in parts as a `text`: `decimals`, `showUnit`, `unitPlacement`,
`unitSpace`, `prefix` and `suffix`. Without `label`, the arc goes alone, as before.

An arc with a value is sent to the panel when the needle moves **or** when the number changes, which
isn't the same: with zero decimals the needle moves a hundred times for each time the number changes.

**The clock doesn't spend what it seems to.** A `clock` is looked at at its `refreshMs` like everything
else, but it is only sent to the panel when the text changes: an `HH:mm` looked at every second travels
once a minute, and a date once a day. The link meter knows it and charges that, not the refresh.

The `format` carries the usual marks, but VibeScreen converts them and not .NET: the service compiles
with `InvariantGlobalization`, so the system templates would say "Monday" and "September" on any
machine. With a table of our own the panel says the same on all of them and the installer doesn't have
to drag ICU along. There is one table per language, and the day and month names come out in the
window's: it tells the service with `set-language` and the service stores it, so the panel keeps it at
startup even when there is no window open. What the theme writes between quotes — the "de" of
`d 'de' MMMM` — belongs to the theme and comes out the same in any language.

| Mark | What it is | Mark | What it is |
|---|---|---|---|
| `HH` `H` | 24-hour hour, with and without zero | `dd` `d` | Day of the month |
| `hh` `h` | 12-hour hour | `ddd` `dddd` | `Wed`, `Wednesday` |
| `mm` `m` | Minutes | `MM` `M` | Month as a number |
| `ss` `s` | Seconds | `MMM` `MMMM` | `Sep`, `September` |
| `tt` | `AM` / `PM` | `yyyy` `yy` | Year |

Text between single quotes goes as is, which is what is needed to write `d 'de' MMMM` without the `d`
of "de" turning into the day. Spanish names are lowercase, as Spanish requires.

**Units are converted.** The backend delivers each quantity in its native unit — degrees Celsius, MHz,
GB — and the layout asks for the one it wants to see it in: `celsius`, `fahrenheit`, `megabytes`,
`gigahertz`, `megabitspersecond`… Without `unit`, the sensor's native one. Conversion only happens
between units of the same dimension; asking for degrees from a load sensor is a warning, not an error.

**The background can be a color or an image.** The image is painted once on the background layer, from
which comes the piece that is restored under each widget when it is repainted, so it costs no link:
what travels to the panel is still the box of the widget that changes. It is placed with `fit`:
`cover` fills and crops, `contain` fits it whole, `stretch` deforms it to the panel and `center` leaves
it at its size. What the image doesn't cover stays in the fallback `color`, which is also what you see
if the file isn't there.

The path can be relative, and then it is resolved **against the theme's folder**; on save, an image
inside that folder is written relative, so theme and image are copied together. A missing image doesn't
prevent opening the theme: it is a warning, like a sensor this machine doesn't have.

**A widget's cost is its box, not its content**, and it is paid on every change. A `chart` changes on
every refresh: a 320x100 strip at 1 Hz is 64,000 B/s, a quarter of the panel's link. `vibescreen run`
tells you in advance how much the theme asks for in the worst case.

## Decisions worth knowing before touching code

**The panel is a constant-throughput byte pipe.** Measured on a Turing 3.5" rev. A: 261,600 B/s,
constant from 1 KB up to the full 307,200 B frame, with a fixed cost per command indistinguishable from
zero. Two rules follow:

- **Dirty rectangles are not merged.** With no cost per command, joining two separate rectangles only
  adds the bytes of the gap. One blit per rectangle.
- **Rendering is planned by byte budget**, not with a free loop. See `ByteBudgetScheduler`.

**And the budget is not the throughput.** The scheduler hands out a fraction of the link —
`ByteBudgetScheduler.DefaultUtilization` — so that the panel keeps answering commands. That is what can
be spent, and it is against that that the editor measures: against the full throughput, a theme that
asked for almost the whole budget came out as "24 % of the panel", in green, and ran in fits and
starts. The constant is public precisely so that the report and the scheduler can't speak of different
numbers.

**The fraction is 0.6, and that number has a measurement behind it.** It used to be a quarter, which
was an intuition — "leaving margin keeps the panel responsive" — until it was checked against the case
that matters: a theme of ten texts, eight bars and two labels, the size of the ones the Python
reference implementation moves without breaking a sweat, asks for 76,550 B/s. That is **29 % of the
panel's throughput**, but it fell outside a budget of a quarter, so the editor would have painted red a
theme the hardware moves with room to spare. With 0.6 it fits at 49 % and 40 % of the link is still
free. `ProtocolCheck` pins it with that specific theme, and also checks that what the report considers
to fit the scheduler dispatches without leaving a queue.

**And within the budget, priority really rules.** When the one that goes first doesn't fit yet, the
distribution stops; it doesn't skip to the next. Skipping let a small low-priority rectangle overtake a
large high-priority one and eat its budget, which is the opposite of what the order promises.

**The service doesn't stay up doing nothing.** Its two loops — the panel and the pipe — only end when
the service is stopped, so one of them returning is already the failure. They are awaited with
`WhenAny` and not with `WhenAll`: `WhenAll` doesn't complete until both do, so if one fails the other
stays alive, the wait never ends and `BackgroundService` never gets to observe the failure. The result
was a service the manager considered healthy while the panel showed a frozen image and the pipe
answered a state that was no longer true. Besides, the panel loop catches **any** exception and
retries: the sensor backend drops to ring 0 and can throw whatever it likes on resume from suspend.

**Commands are always answered, even when they fail strangely.** `catch` lists that enumerate types
always fall short; without a catch-all, an unexpected exception left the command, left the client,
closed the pipe and was discarded unobserved: the user saw "connection closed without answering" and
there wasn't a single line in the log.

**And what arrives over the pipe isn't believed without checking.** On Windows the service runs as
SYSTEM and the access control list gives write access to all local users, because the GUI isn't
elevated. Three caps follow: a message's line can't grow without end, simultaneous clients have a cap,
and `set-layout` rejects network paths — which would make SYSTEM authenticate against whoever is on the
other end —, huge files and anything that isn't a `.json`. See `LayoutPathRule`.

**A layout is a folder, not a loose file.** They live in `%ProgramData%\VibeScreen\layouts` — shared by
the machine, like the service itself, which runs as SYSTEM and has to be able to read them — and each
one goes in its own: `layouts\naruto\naruto.json`, with its resources next to it.

**And the location isn't negotiated with the user.** There is no "save as" and no file dialog: in the
editor and in the Panel tab there is a dropdown with the layouts that exist, and a single save button,
which asks for a name the first time and updates from then on. The rest — where the folder goes, what
the file is called, where the image ends up — is the program's job. A file dialog invites leaving the
layout in Downloads, next to another one, or in a folder that gets renamed tomorrow, and from that come
lost backgrounds and saved paths that stop existing. The name is checked while it is typed —
characters that aren't valid in a folder, reserved system names, one that is already taken — because
finding out after accepting is the worst way to tell it.

The only file dialog left is the one for **importing a theme**, for a layout someone hands you or that
you had from before: it is loaded, asked for a name and saved as one more. Choosing where to *read*
from isn't choosing where to save.

**And who chooses the layout can be chance.** In the Panel tab's dropdown, above the layouts, there is
a "Random: a different one at each start" entry: with it set, the service draws lots for which one to
paint **every time it starts**, which on a normal machine is every time the computer is turned on. At
startup and not on every reconnection: losing the panel and recovering it is the same session, and
changing layout there would be a change nobody asked for and that the user couldn't explain to
themselves. With more than one it never repeats the last time's — with two layouts, pure chance would
repeat half of the starts and give the impression that the option doesn't work —, and what comes out is
written to the configuration as the current layout, so the window, the CLI and the editor keep telling
the truth without having to understand the mode. The decision lives in `LayoutChoice`, with no folders
and no panel, so it can be checked with loaded dice.

**What turns the draw off and what doesn't.** Choosing a specific layout in the panel dropdown turns it
off, because that is saying "this one" and not "another tomorrow"; the dropdown shows one thing or the
other, so it couldn't be otherwise without lying. The rest doesn't touch it: **Reload current** re-reads
from disk the one that is set, which is what it exists for, and **Apply to the panel** from the editor
paints what you are working on and leaves it until the next start, the next draw or the next apply.
That difference travels in the command itself — `set-layout` carries a `chosen` — because from the
service's side the two are identical: the same file being painted. And with the draw on, a second
button appears, **Pick another at random**, which is the only way to see the draw without restarting
the computer.

**And the background image is copied in there, not pointed to where it was.** It is saved as
`fondo.<ext>`, a fixed name because a layout has one background and only one. That is what makes the
layout hold up on its own: the user can move the photo, rename it, delete it or empty the downloads
folder where it was, and the panel keeps painting; and the whole folder is copied to another machine
and works the same. Pointing at the original path gave none of that.

**Configuration files are written whole or not written at all.** `File.WriteAllText` truncates first
and writes after, so a cut in the middle leaves the configuration at zero, and whoever reads it ends up
with the factory values and overwrites it on the next save. The three places that save — the theme, the
service configuration and the window settings — go through `AtomicFile`, which writes next to it and
moves over.

**Orientation and power are persistent state of the device.** They survive the process ending:
rectangles are interpreted in the current orientation, and what is painted on a screen another program
left off isn't seen. The driver sets both every time it initializes instead of inheriting them.

**Brightness, on the other hand, doesn't survive a power off.** On turning on, the panel goes back to its
factory one, so turning off and on from the GUI left the screen at full while the slider kept showing
something else. The driver remembers the last brightness requested and repeats it after every power on;
with the screen off it just notes it down, because with it off there is no brightness to see.

**Detection is passive.** Devices are identified by VID/PID and serial number by reading the registry on
Windows and sysfs on Linux, never by writing to ports to see who answers: on the other end there could
be a UPS or a development board.

**Ring 0 isn't optional, and PawnIO provides it.** Without ring 0 access there is no CPU temperature or
motherboard sensors, which are the app's reason for being. LibreHardwareMonitor gets them through
PawnIO, a Microsoft-signed kernel driver that the library uses but **doesn't install**. Since the end
user shouldn't install anything separately, the VibeScreen installer carries the official PawnIO
installer inside — redistributable unmodified — and runs it with `-install -silent` before starting the
service, warning first on a page of its own. If there is already a PawnIO, it doesn't touch it: its
installer refuses to install over another version. And uninstalling VibeScreen doesn't remove it,
because other programs share it. At run time the driver only needs to be installed: LibreHardwareMonitor
opens `\\?\GLOBALROOT\Device\PawnIO` and carries its Pawn modules embedded. The process that reads
sensors has to be elevated. Without the installer — when developing — `vibescreen sensors` warns on
the error output about what is missing: permissions, PawnIO or processor support.
