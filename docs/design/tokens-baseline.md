# Design token baseline

The current token system, and which parts are Spotify-derived.

Source: `web/src/app/styles/globals.css` (Tailwind v4, CSS-first) and
`web/tailwind.config.js`. Rationale doc: `claude-thoughts/semantic-color-system.md`
(values there are slightly stale).

---

## The headline problem

`globals.css` opens with:

```css
/* Spotify Theme - ShadCN Variables */
```

That is not a stale comment. The theme *is* Spotify's:

| Token | Light | Dark | What it is |
|---|---|---|---|
| `--primary` | `#1db954` | `#1ed760` | **Spotify's brand green** (the file says so) |
| `--accent` | `#1db954` | — | Same green |
| `--ring` | `#1db954` | — | Green focus rings |
| `--background` (dark) | — | `#121212` | **Spotify's dark gray**, per the inline comment |

So the product's primary color, accent, focus ring, and dark canvas are all
inherited from a service that is being removed.

There *is* a separate `--brand` token — purple (`#7c3aed` light / `#9333ea`
dark) — but it is used sparingly: tabs, the sync promo, the Free badge. Product
identity currently reads green, not purple.

> **Decided (brief §12): warm editorial.** These Spotify-derived values are all
> replaced — warm off-white ground (`#FBF8F2`), warm near-black dark canvas
> (`#17140F`), deep teal accent (`#0F5F5C` light / `#5FB3AB` dark). Full palette
> in the brief. The existing `--brand` purple is **not** carried forward.
>
> Note the dark canvas moves from Spotify's cool `#121212` to a warm near-black.
> Keeping warmth in the dark neutrals is what makes this direction hold together
> in dark mode — sliding them to cool grey collapses it back into generic.

---

## Semantic families

The structure is sound and worth keeping. Each family has
`DEFAULT` / `foreground` / `hover`, and status families add `muted`.

| Family | Light | Dark | Used for |
|---|---|---|---|
| `brand` | `#7c3aed` | `#9333ea` | Product accents, tabs, badges |
| `success` | `#16a34a` | `#22c55e` | Completed states, connect actions |
| `warning` | `#d97706` | `#f59e0b` | Pending, attention |
| `error` | `#dc2626` | `#ef4444` | Failures, destructive |
| `info` | `#2563eb` | `#3b82f6` | Processing, informational |

Light mode uses darker variants for contrast on white; dark mode uses lighter
variants. Muted backgrounds are solid `-100` tints in light and 10%-alpha in
dark. Per project docs these meet WCAG AA (4.5:1).

> **Keep this structure.** The semantic layer is the healthy part of the system.
> The problem is `--primary` sitting outside it, pinned to Spotify green.

---

## Known defects to fix in the redesign

**Referenced but never defined.** `tailwind.config.js` maps these to CSS
variables that do not exist in `globals.css`:

| Token | Referenced in Tailwind | Defined in CSS |
|---|---|---|
| `--feature` | 3× | **0** |
| `--chart-1…5` | 1× | **0** |
| `--sidebar-*` | 8× | **0** |

Any utility using them resolves to nothing. Either define them or remove them.

**Hardcoded colors bypassing the token system:**

- `GlobalHeader.tsx` — the "RadioWash" wordmark is `text-green-600`, not a token.
- `icon.tsx`, `apple-icon.tsx`, `opengraph-image.tsx` — `#16a34a` hardcoded in
  the generated favicon and OG images.

These are why the wordmark stays green regardless of theme tokens.

**Theme provider defaults are commented out.** `ThemeProvider` is mounted with
`attribute="class"` and `disableTransitionOnChange`, but `defaultTheme="system"`
and `enableSystem` are commented out in `layout.tsx`. Worth deciding
deliberately — the app currently does not follow OS preference.

---

## Component primitives

Relevant to token work because most components do not consume tokens through a
shared primitive.

`components.json`: shadcn **new-york**, baseColor `neutral`, `cssVariables: true`,
icon library `lucide`.

**Installed primitives — only four:**
`button.tsx`, `dropdown-menu.tsx`, `sonner.tsx`, plus custom `ClientDate.tsx`
and `theme-toggle.tsx`.

**Missing:** card, dialog, input, select, badge, tabs, skeleton, tooltip,
separator, avatar.

Consequently most UI is raw `<button>` / `<select>` / `<input>` with ad-hoc
Tailwind. Cards, badges, and tabs are all hand-rolled per usage, which is the
main source of visual inconsistency — and it means token changes do not
propagate reliably.

> **For the redesign:** establishing real primitives is as much of the work as
> choosing colors. A token system only holds if components consume it centrally.

---

## Typography and spacing

No custom type scale is defined — the app uses Tailwind defaults throughout.
No documented spacing rhythm; padding and gaps are chosen per component.

> Both still need building — there is no existing system to preserve. But the
> chosen direction constrains them (brief §12): it needs a **display serif**
> paired with a body sans, and it needs **tight corner radii** (`3px`, down from
> the current `0.5rem`). Soft rounded cards on a warm ground is the generic look
> this direction exists to avoid.
