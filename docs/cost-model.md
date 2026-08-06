# RadioWash Cost Model: Profit & Loss by User Milestone

_August 2026. Companion interactive calculator (adjustable assumptions, live P&L): [claude.ai artifact](https://claude.ai/code/artifact/9900e281-a565-4283-9a05-e1ca88f5520a)._

## Key findings

RadioWash is a structurally cheap business with one economic asymmetry worth watching. The infrastructure floor is about $26/month, break-even arrives at roughly **155 registered users** at a 5% free→paid conversion, and margins climb past 85% from there — at 10,000 users the model shows ~$2,200/month profit on ~$85/month of cost. Software is not the constraint.

The asymmetry: **the expensive operation is free.** Playlist cleaning and cross-service copying have no subscription gate (`CleanPlaylistService.CreateJobAsync` performs no subscription check), while the only paid feature — recurring sync — is comparatively cheap to serve. Every registered user can burn provider API calls, Hangfire worker-time, and permanent database rows at zero price, forever. At today's scale this is a growth loop, not a problem. The model says it stays affordable to ~50k users; whether it stays _safe_ depends on the two unbounded tables and one abuse vector described below.

Because costs are nearly flat, the P&L is hypersensitive to conversion, not scale. The break-even conversion rate at 1,000 users is **0.8%**; at 10,000 it is **0.2%**. Almost any conversion rate works. The real risk register is operational, not financial.

## Cost structure

**Fixed floor (verified against Azure Cost Management, August 2026 — the first month this subscription accrues real charges; July billed $0 on credits).**

| Line                                                  | $/mo     | Source                                                             |
| ----------------------------------------------------- | -------- | ------------------------------------------------------------------ |
| App Service B1 (API + Hangfire + SignalR, 1 instance) | ~13      | actual: $1.21 in first 3 days of Aug ≈ $12.40/mo pace; list $13.14 |
| Container Registry Basic                              | ~5       | actual pace $4.70/mo                                               |
| Container Apps (web, scale-to-zero, 0.25 vCPU)        | 0        | inside the 180k vCPU-s monthly free grant                          |
| Log Analytics, bandwidth                              | 0        | actuals $0; first 5 GB ingestion free                              |
| Apple Developer Program                               | 8.25     | $99/yr flat                                                        |
| Supabase, Sentry, Plausible                           | 0        | all on free tiers today                                            |
| **Total**                                             | **~$26** |                                                                    |

**Revenue.** $5.00/month single plan. Stripe takes 2.9% + 30¢, leaving **$4.56 net per subscriber per month** — a 9% payment-processing tax that is the largest marginal cost in the whole business. At 5% monthly churn, subscriber lifetime value is ~$91.

**Variable costs.** Provider APIs are free (Spotify) or flat (Apple, $99/yr), so variable cost is really three things: database growth, compute for the nightly sync, and observability/analytics volume. Database growth is the compounding one: `TrackMappings` writes ~550 bytes per track per job and `PlaylistSyncHistory` one row per sync config per day, and **neither table has any retention**. At 10,000 users with default activity assumptions that is ~200 MB/month of permanent growth.

## P&L at each milestone

Default assumptions: 5% conversion, 30% of free users active monthly, 1.5 clean jobs per active user at 80 tracks, 3 sync playlists per subscriber, 12 months of accumulated history. All adjustable in the [calculator](https://claude.ai/code/artifact/9900e281-a565-4283-9a05-e1ca88f5520a).

| Users  | Subs  | Net revenue | Total cost | Profit/mo    | Margin | Break-even conv. |
| ------ | ----- | ----------- | ---------- | ------------ | ------ | ---------------- |
| 100    | 5     | $23         | $35        | **−$12**     | —      | 7.7%             |
| 1,000  | 50    | $228        | $35        | **+$193**    | 77%    | 0.8%             |
| 10,000 | 500   | $2,278      | $85        | **+$2,193**  | 88%    | 0.2%             |
| 50,000 | 2,500 | $11,388     | $205       | **+$11,182** | 89%    | 0.1%             |

The only loss-making milestone is the first hundred users, and the loss is a rounding error ($12/month). From 1,000 users on, cost is noise against revenue.

## Tier cliffs on the way up

The cost curve is a staircase, not a line. First trigger points at default assumptions:

- **~1,300 users — Plausible** moves past the $9 entry tier (it has no free tier once the trial ends; budget $9–19/mo from launch).
- **~1,600 users — Supabase Free → Pro ($25/mo)**: accumulated `TrackMappings` + sync history crosses the free 500 MB database cap. This is the first _earned_ upgrade, and it is driven almost entirely by tables nothing ever deletes.
- **~16,000 users — App Service B1 → B2 ($26/mo)**: every daily sync fires at 00:01 UTC (`SyncTimeCalculator` pins next-run to 00:01). B1's ~5 Hangfire workers clear roughly 2,400 syncs in a two-hour window; subscriber growth outruns that here.
- **~29,000 users — Sentry Free → Team ($26/mo)**: past 5k errors/month the free plan _silently drops events_ — the failure mode is blindness, not a bill.
- **~32,000 users — Supabase Pro + disk overage**, and **B2 → B3**; **~64,000 — B3 → P0v3**, where vertical scaling runs out and horizontal scaling is blocked until SignalR gets a backplane and Hangfire moves out of the web process.

## Implications

**1. The business case needs no work; the abuse case does.** With break-even conversion under 1% from 1,000 users, pricing and margin are solved. The unmodeled risk is that job creation is both free and un-rate-limited (only `/subscription/checkout` has a rate limiter): a single hostile or scripted account can enqueue unlimited jobs, each burning Spotify quota (a cross-service copy can issue ~200 sequential searches before matching begins), worker-hours, and permanent rows. A per-user daily job cap would close the tail risk without touching the growth loop.

**2. Add retention before ~5,000 users.** `TrackMappings` and `PlaylistSyncHistory` growth is what drags the Supabase upgrade forward and what makes every sync slower (`GetByJobIdAsync` loads a config's entire accumulated mapping history into memory on each run). A 90-day retention job — the pattern already exists for the Stripe webhook tables — defers the Pro upgrade and flattens the sync path.

**3. Stagger the sync herd before ~10,000 users.** All syncs firing in the same second means the compute ceiling is set by peak, not average, load. Spreading configs across a window (even hashing user id into 00:00–02:00) roughly doubles effective capacity per tier — cheaper than B2.

**4. Two housekeeping items have already come due.** ACR Basic is at 10.72 GB of its 10 GB included storage (one image per merge, never purged) — overage is pennies ($0.10/GB/mo) but a purge task or `az acr` retention policy stops the drift. And Sentry's browser config (`replaysOnErrorSampleRate: 1.0`, session replay + logs enabled) is the most likely surprise line item after an upgrade to Team; worth revisiting sample rates then, not now.

**5. Watch conversion, not cost.** The single number that decides this P&L is free→paid conversion, and today there is no instrumentation distinguishing "registered," "active free," and "converted" cohorts. Plausible custom events (or a simple weekly SQL rollup) tracking that funnel is worth more than any cost optimization on this list.

---

### Sources & method

Azure actuals: Cost Management query, subscription `315ef1…`, resource group `radio-wash_group`, Aug 2026 MTD; App Service plan/ACR SKUs via `az`. Workload constants from code: batch sizes (`AppleMusicService`, `PlaylistCopier.MaxPrefetchIsrcs`, `TrackMatcher`), sync cron (`SyncSchedulerService`), entity shapes (`TrackMapping`, `PlaylistSyncHistory`). Public pricing (verified Aug 2026): [Supabase](https://supabase.com/pricing) (Free 500 MB DB / 50k MAU; Pro $25 + $0.125/GB past 8 GB), [Sentry](https://sentry.io/pricing/) (Free 5k errors; Team $26), [Plausible](https://plausible.io/#pricing) ($9 at 10k pageviews, no free tier), [Azure App Service Linux](https://azure.microsoft.com/en-us/pricing/details/app-service/linux/) (B1 $13.14 / B2 $26.28 / B3 $52.56), [Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/billing) (180k vCPU-s free grant). Estimated, clearly-assumption-driven inputs (activity rates, error rates, log volume) are sliders in the calculator so they can be corrected as real data arrives.
