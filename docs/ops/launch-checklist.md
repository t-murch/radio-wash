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

- [x] **Archive the $2.99 live price** (`price_1SLDBHGUEfIc3dIUsYLh8zEH`) —
      done 2026-08-09 via dashboard; verified `active: false` via the API.

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

- [x] **Rotate the database password** — done 2026-08-09. Rotated in
      Supabase, updated in both the `SUPABASE_DB_CONNECTION` GitHub secret
      and the App Service setting, and redeployed. With the stale ACR image
      already deleted, every credential that ever leaked through git history
      or baked images is now invalid.

## Email / DNS

Sending goes through Resend on the dedicated subdomain
`updates.radiowash.com` — deliberately not the root, so the root domain's
reputation is isolated if anything goes wrong.

- [x] **Resend domain verified with SPF/DKIM** — done 2026-08-09. DKIM
      (`resend._domainkey.updates`), SPF + feedback MX (`send.updates`) all
      resolve publicly. The root's DMARC (`p=quarantine`, relaxed alignment)
      covers the subdomain; no extra DMARC record needed.

- [x] **Point hosted Supabase at Resend SMTP** — done 2026-08-09.
      Custom SMTP enabled: `smtp.resend.com:465`, sender
      `sign-in@updates.radiowash.com`. Verified end-to-end by triggering a
      real magic link from the hosted project through Resend. (Cosmetic:
      sender name is "Radiowash Team" — brand spells it "RadioWash".)

- [ ] **Set the hosted magic-link email template.** `config.toml` and
      `supabase/templates/magic-link.html` only govern local; paste the
      template's content into Authentication → Email Templates → Magic Link
      in the dashboard. The template must link to
      `{{ .SiteURL }}/auth/confirm?token_hash={{ .TokenHash }}&type=magiclink`
      — the default template's link does not match our confirm route.

- [ ] **`support@radiowash.com` must receive mail** — `/privacy` and
      `/terms` name it as the contact and account-deletion channel, and
      Resend only handles sending. GoDaddy's email forwarding product is
      retired, and Resend's receiving is webhook-based (an endpoint we'd
      have to build, plus a retrieval API call for the body — too much
      machinery for a support inbox). The root domain has **no MX records
      at all** (verified), so a forwarding-only MX service drops in with
      zero conflicts. Recommended: ImprovMX free tier —

      1. improvmx.com → add domain `radiowash.com`, alias `support` →
         personal inbox.
      2. GoDaddy DNS, on the **root** (`@`):
         - MX `mx1.improvmx.com` priority 10
         - MX `mx2.improvmx.com` priority 20
         - TXT `v=spf1 include:spf.improvmx.com ~all`
      3. Send a test mail to `support@radiowash.com` and confirm it arrives.

      None of this touches `updates.radiowash.com`, so sending is
      unaffected.

## Business

- [x] **Refund promise resolved** — done 2026-08-09. Decision: no pro-rated
      refunds. The terms no longer promise a refund on discontinuation and
      the Payment section states plainly that payments already made are not
      refunded; cancellation takes effect at period end with access until
      then, matching the Stripe cancel-at-period-end configuration.

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
