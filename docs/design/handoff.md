# Handoff

How to hand this bundle to a design collaborator, and in what order.

The bundle is deliberately more than fits in one sitting. Feeding it all at
once produces worse work than sequencing it — the screenshots in particular
are actively misleading without framing.

---

## The framing message

Lead with this. Paste it verbatim; the last clause is doing real work.

> RadioWash is a tool that takes a playlist containing explicit tracks and
> produces a clean version of it — same songs, radio edits substituted where
> they exist.
>
> It is becoming **Apple Music–only**. Spotify is being removed entirely:
> Spotify's API now caps development-tier apps at 5 users, which is not a
> product we can ship.
>
> There are **zero production users**, so treat this as a first launch, not a
> redesign. No migration, no legacy data, no backward compatibility.
>
> The screenshots in this bundle show what exists **today** — a Spotify-centric
> app. They are reference for what is being *removed*, not a baseline to
> respect or refine.
>
> The visual direction is already decided (brief §12): warm editorial — warm
> off-white ground, serif display, deep teal accent. Palette values are in the
> brief.

Then attach `brief.md` and `tokens-baseline.md`. Nothing else yet.

---

## Order of work

### 1. The display serif — before any layout

The chosen direction rests on a serif that has not been picked. The exploration
used a system fallback stack, so nothing seen so far shows the direction at full
strength.

Ask for **3–4 candidate faces**, shown on real RadioWash content: a playlist
name, a heading, a track title, and a large tabular figure (the "88 of 187"
progress). It needs warmth and editorial character without tipping decorative,
and it has to hold up small.

Scoped, low-risk, and it de-risks the whole direction before anything inherits
it.

### 2. Onboarding — the first real screen

**Why first:** it is the highest-value new screen, it does not exist yet (so
there is no before-screenshot to anchor against), and it is where the warm
editorial direction either works or does not. Finding that out here is cheaper
than finding out after twelve screens inherit it.

Give it brief §6 and §13. The route covers four moments:

1. Sign in — Apple, Google, or email
2. Explain, then authorize Apple Music
3. Authorized, nothing cleaned yet → straight into picking a first playlist
4. Blocked — no Apple Music subscription, so step 2 cannot complete

Emphasize moment 2. An unexplained second permission dialog is where people
drop out, and today it is a card buried on a dashboard.

### 3. Dashboard — with its screenshots

Only now hand over the before-images, and only these four:

- `dashboard-apple-only__light__desktop.png` — **lead with this one.** A user
  with only Apple Music connected lands on the Spotify tab and is told "Connect
  Spotify to Get Started," while their working provider sits unselected one tab
  over. It is the clearest single piece of evidence in the bundle.
- `dashboard-both-connected__light__desktop.png` — the full dual-provider UI
  being collapsed
- `dashboard-empty__light__desktop.png` — what a new user sees today
- `dashboard-both-connected__dark__desktop.png` — dark treatment

Say plainly what is being removed: two provider cards become one connection
state, the tab bar disappears, and the clean-vs-copy radio pair becomes a single
clean action. That is roughly the top third of the screen.

### 4. Everything else

Job detail (four states — see §7), sync, subscription, landing. By this point
the direction is established and these are applications of it.

Hand over `constraints.md` **here**, not earlier. It answers "can I show a track
count?" (no) and "where does the Open in Apple Music button go?" (nowhere) —
questions that only arise once someone is drawing specific components.

---

## Screenshot rules

- **Never hand over the folder.** 76 images with no framing reads as a style
  reference.
- Pass 2–4 per screen, when that screen is the subject.
- Every time, say what is wrong with the current version — pull the relevant
  point from brief §4.
- `screen-inventory.md` maps every screen and state to its filenames.

---

## Two decisions that add screens

Both are settled, and both expand the scope beyond what the screenshots show.

**Email sign-in is a magic link** (brief §13). Four screens that exist nowhere
today: enter-email, check-your-inbox, expired-link, and the
opened-on-another-device case. That last one is the one usually forgotten and
the one users hit most. It also means a magic-link user arrives at onboarding
already mid-sequence, having crossed a device boundary — worth raising when
onboarding is drawn (step 2 above), not later.

**Sharing is revived, but rebuilt** (brief §14). It shares a *generated card*,
not a link — Apple library playlists have no public URL, so the old URL-sharing
components cannot be restored. Treat this as a new feature rather than a
restoration. It belongs to the job-completion moment, so it comes up in step 4.

---

## One caveat on this brief's authority

The brief mixes two kinds of statement, and they are not always visually
distinguishable:

- **Verified facts** — API constraints, token values, what the code does. These
  were checked against the source and can be trusted.
- **Decisions** — recorded in the §2 table and in §12. These are settled.
- **Product judgments** — the north star wording, where Auto-Sync should
  appear, the read that the four-way match chip over-exposes implementation
  detail. These are inferences, not instructions.

If a designer pushes back on something and the argument sounds reasonable,
check which category it falls into before defending it. The third category is
meant to be argued with.
