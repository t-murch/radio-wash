# Apple-only launch checklist

Pre-deploy runbook for the `feat/apple-only` redesign. Grouped by system;
within a group, order matters where noted. Items marked **done** were
completed on 2026-08-09 and are listed so the record lives here rather than
in a chat log.

## Stripe (live mode)

- [x] **Point production at the $5/month live price** — done 2026-08-09.
      `Stripe__PricePlanId` in the App Service settings and the
      `STRIPE_PRICE_PLAN_ID` GitHub secret both now hold
      `price_1TNdUVGUEfIc3dIU57v3H50g` ($5.00/month). Production previously
      pointed at the live $2.99 price while every shipped surface says $5 —
      checkout would have silently undercharged.

- [x] **Repoint the live webhook endpoint** — done 2026-08-09 via dashboard.
      It previously targeted `https://radiowash.com/api/subscription/webhook`
      (the *frontend*, a 404 — every live event was being swallowed). Now
      verified via the API: URL is
      `https://api.radiowash.com/api/subscription/webhook`, status enabled,
      with all six events the processor handles, including the previously
      missing `invoice.payment_succeeded`. The endpoint was edited in place,
      which keeps the signing secret — if webhook deliveries show 400 after
      launch, the secret changed after all: copy the endpoint's current
      `whsec_…` into the `STRIPE_WEBHOOK_SECRET` GitHub secret and the App
      Service `Stripe__WebhookSecret` setting.

- [x] **Save a Customer Portal configuration in live mode** — done
      2026-08-09 via dashboard. Verified via the API: one active default
      configuration with invoice history, payment-method update, and
      subscription cancel enabled. "Manage billing" now has something to
      open in production.

- [ ] **Archive the $2.99 live price** (`price_1SLDBHGUEfIc3dIUsYLh8zEH`) so
      nothing can accidentally reference it again:

      ```bash
      stripe prices update price_1SLDBHGUEfIc3dIUsYLh8zEH -d "active=false"
      ```

## Azure

- [x] **Delete the stale `latest` image from ACR** — done 2026-08-09.
      Pushed manually in May 2025, before `.dockerignore` existed — it
      contained that era's `appsettings.json`/`appsettings.Development.json`
      (Supabase DB password, Spotify secrets; no Stripe keys, which didn't
      exist yet). Production was pinned to a SHA tag, so deletion affected
      nothing. With the DB password rotation (below), nothing the image
      leaked stays valid. Locally built images no longer bake the file at
      all as of the `.dockerignore` fix.

- [ ] Optional cleanup: the App Service still carries dead settings from
      removed features — `Spotify__ClientId`, `Spotify__ClientSecret`,
      `Spotify__RedirectUri`, and the legacy `Jwt__*` block. No code reads
      the Spotify ones; verify `Jwt__Secret` is unread before removing it.
      The deploy workflow no longer re-injects the Spotify pair.

## Supabase (hosted)

Order matters: reset first, then rotate, then deploy.

- [ ] **Run `docs/ops/reset-hosted-db.sql`** in the dashboard SQL editor with
      the API stopped. It prints row counts (including how many `auth.users`
      accounts it is about to delete) before anything drops — read the
      NOTICEs, then COMMIT. The next API deploy applies `InitialAppleMusic`
      and seeds the Sync Plan from the (now-$5) configured price.

- [ ] **Set `otp_expiry = 900`** (Authentication → Providers → Email). The
      shipped copy promises a 15-minute magic link in five places;
      `config.toml` only governs local.

- [ ] **Rotate the database password** (Settings → Database). It appeared in
      git history before `e7b1563` and in the stale ACR image. Then update
      *both* consumers: the `SUPABASE_DB_CONNECTION` GitHub secret and the
      App Service `ConnectionStrings__DefaultConnection` setting (or just
      the secret, followed by a deploy).

## Email / DNS

- [ ] **`sign-in@radiowash.com`** as the magic-link sender with SPF/DKIM
      (Supabase custom SMTP), or the emails land in spam regardless of copy.
- [ ] **`support@radiowash.com`** mailbox — `/privacy` and `/terms` name it
      as the contact and account-deletion channel.

## Business

- [ ] **Confirm the refund promise.** The shipped terms promise pro-rated
      refunds if the service is discontinued. Keep it or amend the terms
      before launch — it is a real commitment either way.

## After deploy — verification

1. `SELECT "MigrationId" FROM "__EFMigrationsHistory"` → exactly
   `20260807235847_InitialAppleMusic`.
2. `SELECT "Name", "StripePriceId", "PriceInCents" FROM "SubscriptionPlans"`
   → Sync Plan, `price_1TNdUV…`, 500. The API logs a startup warning if the
   configured price and the seeded row ever diverge.
3. Full path on production: sign up → connect Apple Music → clean a playlist
   → subscribe (real card, then refund/cancel from the dashboard) → confirm
   the webhook delivery shows 200 in the Stripe dashboard, and the
   subscription row appears without waiting for the hourly reconciliation
   sweep.
