# Making animated GIFs for a VibeScreen panel

A guide for language models (and people) asked to create or convert a GIF for an image widget in a
VibeScreen theme. Every number here comes from the app's own code and was checked against it on
2026-10-27.

## What matters, in one paragraph

The panel is a small USB screen fed over a slow serial link: about **261,600 bytes per second**, of
which the app hands out **196,200 B/s** (75 %) to the whole theme. Every time a GIF moves to its next
frame, the app resends the **entire widget box**, at 2 bytes per pixel (RGB565). So the cost of a GIF
is `box width × box height × 2 × frames per second`, whatever is inside the frame. Size and frame
rate are what you're budgeting; file size, colours and GIF compression barely matter.

## Hard limits

| Limit | Value | If broken |
|---|---|---|
| Frames | 256 at most | The GIF isn't drawn: the editor flags it and the box stays empty |
| Decoded size | `width × height × 4 × frames` ≤ 64 MiB, for the GIF itself and again for the box it's scaled to | Same |
| Widget refresh (`refreshMs`) | 100 ms at least, so **10 frames per second at most** | The whole theme is rejected when it loads |

Full-panel sizes run into the decoded-size cap quickly: at 480×320 that's 109 frames.

## The link budget

`bytes per second = width × height × 2 × fps`. The whole theme shares 196,200 B/s.

| Box | Bytes per frame | 10 fps | 5 fps | 2 fps | 1 fps |
|---|---|---|---|---|---|
| 32×32 | 2,048 | 10 % | 5 % | 2 % | 1 % |
| 48×48 | 4,608 | 23 % | 12 % | 5 % | 2 % |
| 64×64 | 8,192 | 42 % | 21 % | 8 % | 4 % |
| 96×96 | 18,432 | 94 % | 47 % | 19 % | 9 % |
| 128×128 | 32,768 | 167 % | 84 % | 33 % | 17 % |
| 160×90 | 28,800 | 147 % | 73 % | 29 % | 15 % |
| 240×160 | 76,800 | 391 % | 196 % | 78 % | 39 % |
| 480×320 (whole panel) | 307,200 | — | — | — | 157 % |

Rules of thumb:

- **Keep one GIF under about half the budget.** Text, bars and charts need the rest. The editor's
  meter turns amber above 60 % and red above 100 %.
- **Highest sensible frame rate:** `fps = 98,100 / (width × height × 2)` for half the budget, capped at
  10.
- **Whole-panel animations don't work.** One 480×320 frame takes 1.6 s of the entire budget.
- Two GIFs add up. A 64×64 at 10 fps (81,920 B/s) next to a 160×90 at 5 fps (144,000 B/s) is 115 %
  of the budget, although each fits on its own.

Over budget, nothing breaks. The animation keeps its speed and **skips frames**, and every other
widget waits longer for its turn. It just looks choppy.

## Timing

- **Frame durations come from the file.** Durations of 10 ms or less (and 0) play as 100 ms, like
  browsers do.
- **Set the widget's `refreshMs` to the GIF's shortest frame duration.** The default is 1000, which
  shows only one frame per second of a 10 fps GIF. The animation doesn't slow down; it skips frames.
- **Use durations of 100, 200, 300, 500 or 1000 ms.** Anything shorter than 100 ms can't be shown, and
  multiples of 100 ms keep frames evenly spaced.
- **A repeated frame still costs a whole box.** Fold identical consecutive frames into one longer
  frame instead: a 500 ms pause is one frame, not five.
- **GIF frame-difference optimisation** (`gifsicle -O3`, ffmpeg's `diff_mode`) shrinks the file but
  not the link cost. Use it if you like; it changes nothing on the panel.
- **The loop count is ignored.** A GIF loops forever, timed by the clock, so two GIFs with the same
  total length stay in step.

## Size and scaling

- **Make the GIF exactly the size of its widget box.** Then nothing is rescaled and `fit` doesn't
  matter. Ask for the box size, or read `rect` (`[x, y, width, height]`) from the theme's JSON.
- **Panel sizes:** the Turing 3.5" is 480×320 landscape or 320×480 portrait. Read `display` in the
  theme for the actual one.
- **If it must be scaled:** enlarging 2× or more in both directions uses nearest-neighbour (crisp, good
  for pixel art); anything else is smoothed. Pixel art therefore looks best at an exact integer scale
  of 2× or more. `fit` decides the rest: `contain` (the default) fits it whole, `cover` crops, `stretch`
  distorts, `center` doesn't scale.
- **It's a 3.5" screen, about 165 pixels per inch.** Details under 3–4 px and text under about 10 px
  tall won't read.

## Colour and transparency

- **The panel shows RGB565:** 32 levels of red and blue, 64 of green. Subtle gradients band, even when
  the editor preview looks smooth. Flat colours and strong contrast look best.
- **One palette for all frames.** Per-frame palettes make colours flicker between frames.
- **Dithering costs no link, but noise looks grainy at this size.** Prefer none for flat art, and
  ordered (Bayer) over error diffusion for video.
- **GIF transparency is on or off.** Transparent pixels show the theme's background, but anti-aliased
  edges keep the colour they were drawn over and leave a halo. **Draw the animation on the theme's
  background colour and keep it opaque.** The default theme uses `#0A0C10`.

## Recipe 1: draw it in Python (Pillow)

```python
"""Make GIFs that suit a VibeScreen panel. Requires Pillow (pip install pillow)."""
import math
from PIL import Image

LINK_BUDGET = 196_200      # bytes/s the scheduler hands out (261,600 B/s x 0.75)
BYTES_PER_PIXEL = 2        # RGB565 on the wire
MIN_FRAME_MS = 100         # a widget is looked at most every 100 ms
MAX_FRAMES = 256
MAX_DECODED = 64 * 1024 * 1024


def cost(width, height, fps):
    """Bytes per second this GIF takes from the link: the whole box, every frame shown."""
    return width * height * BYTES_PER_PIXEL * fps


def max_fps(width, height, share=0.5):
    """Highest frame rate that keeps this GIF within `share` of the link budget."""
    return min(1000 / MIN_FRAME_MS, LINK_BUDGET * share / (width * height * BYTES_PER_PIXEL))


def save_panel_gif(frames, path, size, frame_ms=100, pixel_art=False, colors=256):
    """Resize frames to the exact widget box, merge repeats and save a looping GIF.

    frames: list of PIL images. size: (width, height) of the widget box in panel pixels.
    frame_ms: how long each frame shows; rounded up to a multiple of 100 ms.
    """
    width, height = size
    frame_ms = max(MIN_FRAME_MS, math.ceil(frame_ms / MIN_FRAME_MS) * MIN_FRAME_MS)
    resample = Image.NEAREST if pixel_art else Image.LANCZOS

    fitted = [f.convert("RGB").resize((width, height), resample) for f in frames]

    # One palette for every frame, so colours don't flicker between frames. Built from all of them
    # (up to 64, evenly spread): from the first alone, a colour that appears later is lost.
    sample = fitted[:: max(1, len(fitted) // 64)]
    sheet = Image.new("RGB", (width, height * len(sample)))
    for i, frame in enumerate(sample):
        sheet.paste(frame, (0, i * height))
    palette = sheet.quantize(colors=colors, method=Image.MEDIANCUT)
    quantized = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in fitted]

    # A repeated frame still costs the whole box on the link: fold it into the previous one.
    # After quantizing, because that is what goes into the file.
    out, durations = [], []
    for frame in quantized:
        if out and frame.tobytes() == out[-1].tobytes():
            durations[-1] += frame_ms
        else:
            out.append(frame)
            durations.append(frame_ms)

    if len(out) > MAX_FRAMES:
        raise ValueError(f"{len(out)} frames; the panel takes at most {MAX_FRAMES}")
    if width * height * 4 * len(out) > MAX_DECODED:
        raise ValueError("decoded size over 64 MiB: fewer frames or a smaller box")

    out[0].save(path, save_all=True, append_images=out[1:], duration=durations, loop=0,
                disposal=1, optimize=False)

    shown_fps = 1000 / min(durations) if len(durations) > 1 else 0
    return {"frames": len(out), "durations_ms": durations,
            "link_bytes_per_s": cost(width, height, shown_fps),
            "share_of_budget": cost(width, height, shown_fps) / LINK_BUDGET}
```

Draw each frame on the theme's background colour, at the box size or larger (it's resized down with
Lanczos). Pass `pixel_art=True` for hard-edged art, which keeps nearest-neighbour edges.

Two traps this code avoids, both seen while testing it:

- **A palette built from the first frame only.** Colours that appear later get mapped to the nearest
  colour that is in the palette, frames that should differ end up identical, and the GIF writer merges
  them. A red-then-blue animation came out as one red frame.
- **Comparing frames with `ImageChops.difference(...).getbbox()`.** On RGBA images it only looks at
  the alpha channel, so every opaque frame looks identical and the whole animation folds into one
  frame. Compare the bytes.

Check the result: open the file again and count its frames and durations (with Pillow, `n_frames` and
`info["duration"]` on each frame) before handing it over.

## Recipe 2: convert a video or an existing GIF (ffmpeg)

Pick the box size and frame rate from the budget table first, then:

```bash
ffmpeg -i input.mp4 -vf "fps=5,scale=160:90:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=5" -loop 0 output.gif
```

- `fps=` sets the frame rate. Use 10, 5, 2 or 1, so frames last 100, 200, 500 or 1000 ms.
- `scale=W:H` is the exact widget box. If the source has another aspect ratio, crop first
  (`crop=…`) instead of distorting it.
- `palettegen`/`paletteuse` build one palette for the whole clip.
- Trim to a short loop (`-ss` and `-t`). Seconds × fps must stay within 256 frames, and loops of
  2–5 s are usually enough.

## The widget in the theme JSON

```json
{ "type": "image", "id": "spinner", "rect": [16, 16, 64, 64], "path": "spinner64.gif", "refreshMs": 100 }
```

- `rect` is `[x, y, width, height]` in panel pixels. Width and height should equal the GIF's size.
- `refreshMs` is the GIF's shortest frame duration.
- `path` can be relative to the theme folder. When the theme is saved from the editor, the image is
  copied into the theme's `imagenes` folder.
- `priority` (optional, higher goes first) decides who waits when the link is full.

## Checklist before handing a GIF over

1. Its size is exactly the widget box.
2. `width × height × 2 × fps` is under about 98,000 B/s (half the budget), and under 196,200 together
   with the theme's other animations.
3. No more than 256 frames, and `width × height × 4 × frames` ≤ 64 MiB.
4. Frame durations are multiples of 100 ms. Identical consecutive frames are merged.
5. The widget's `refreshMs` equals the shortest frame duration.
6. It's drawn opaque on the theme's background colour, with one palette for all frames.
7. It loops cleanly: the last frame leads naturally into the first.
