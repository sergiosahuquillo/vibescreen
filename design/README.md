# The design

This is not documentation: **it is the specification**, and it is verified on every build.

`app.css` and `app.js` are the files of the OpenDesign export exactly as they came out — `app.css`
from the export of 6 October 2026 and `app.js` from 26 September, which the 6 October one didn't
change — without touching a single line. The rest of the
export (the screenshots, the three typefaces and the sample images) is left out: the fonts are already
embedded in `VibeScreen.App` and the images say nothing the CSS doesn't say better.

## Why it lives in here

Because while it lived on a desktop, the only thing comparing the design with the code was someone
looking at two screenshots, and that is how things like these slipped through:

- `.prop-sub` asks for `overflow: hidden`, and nobody read it: the body of the collapsible section ate
  the curve of the frame.
- `.prop-sub` asks for `background: var(--surface-2)`, and the code painted `--surface`: the section came
  out the same color as the column and looked stuck on top instead of fitted in.
- `.check-box` asks for 18 px and corners of 5, and the window painted Fluent's 20 px and corners of 3,
  because the style that said otherwise didn't win and nobody had checked it.

`tools/VibeScreen.ThemeCheck` now finds all three in under a second:

```
dotnet run --project tools/VibeScreen.ThemeCheck
```

And a fourth one that showed where it wasn't looking: the 6 October export only moved pieces around —
the inspector column to full height, the editor bar into groups — and the verifier passed green
without a line being touched, because it measured individual controls and not where each one goes.
Since then it mounts the whole editor and measures how it shares out the space.

## How to read it

- The `:root` of `app.css` holds the variables of the dark palette, and `:root[data-theme="light"]`
  only what changes in the light one. Each has its brush in `src/VibeScreen.App/Palette.cs`.
- The accent is **not** in the CSS: the prototype writes it with JavaScript from the `ACCENTS` table in
  `app.js`, which are the eight in `Palette.Accents` with their pair of tones.
- What only exists between pieces — the column that runs top to bottom, Properties taking the leftover
  space, the theme field growing up to its cap — is measured with the editor mounted, and each piece
  is looked up by the name the CSS gives it: `inspector`, `editor-bar`, `wlist`, `props-panel`…
- Rules inside a `@media` describe the narrow window. The verifier leaves them out.

## When another export arrives

First of all, a diff of the new `app.css` against the one here: an export may fail nothing, like the
6 October one, and whatever changes without any check looking at it is the first thing to teach the
verifier. Then the two files are replaced and it is run again. What fails is the list of what has to
change in the code, in order; what doesn't show up there is something the export doesn't say, and you
have to ask before inventing it.

The design rules the **look**, not the content: which fields each place has and what they say is
decided by the app. That the prototype shows two fields in Screen means that is how fields are
dressed, not that the other seven are surplus.
