# RadioWash design bundle

Seed material for redesigning RadioWash as a single-provider **Apple Music** app.

| File | What it is |
|---|---|
| `brief.md` | The brief. Start here. |
| `screen-inventory.md` | Every screen, every state, with the screenshot filenames. |
| `constraints.md` | What Apple Music can and cannot do, and what each limit forces in the UI. |
| `tokens-baseline.md` | Current design tokens, and which are Spotify-derived. |
| `screenshots/` | 76 PNGs of the current app: 19 screens × light/dark × desktop/mobile. |
| `screenshots/manifest.json` | Machine-readable index of the capture run. |

Screenshots show the **current** Spotify-centric app. They are the "before,"
not a target. Regenerate them with `pnpm design:mock` + `pnpm design:web` +
`pnpm design:capture` (see `tools/design-capture/README.md`).
