# Stripe Payments Runbook

Operational reference for the subscription/payment system. Code lives in
`api/Services/Implementations/Stripe*`, `api/Controllers/SubscriptionController.cs`, and
`web/src/app/subscription/`.

## Architecture in one paragraph

Checkout sessions are created server-side (`POST /api/subscription/checkout`) with the price
resolved from the local `SubscriptionPlans` table — client price ids are never trusted. Stripe
redirects back to `/subscription/success?session_id=...`, where the frontend calls
`POST /api/subscription/checkout/complete` to reconcile the subscription immediately instead of
waiting for the webhook. Webhooks land on `POST /api/subscription/webhook`: signature failures
return 400 (permanent reject), processing failures return 500 so Stripe redelivers for up to
3 days; a `ProcessedWebhookEvents` claim row guarantees exactly-once processing while allowing
failed events to be re-claimed. Subscription state is upserted from Stripe's view
(`SyncFromStripeAsync`), so event ordering doesn't matter. Cancellation sets
`cancel_at_period_end` on Stripe; access continues until `customer.subscription.deleted`
arrives. An hourly reconciliation job sweeps local state against Stripe both ways.

## Production configuration

All secrets are injected from GitHub Secrets into Azure App Settings by
`.github/workflows/api-deploy.yml` — the on-disk `appsettings*.json` files are gitignored and
must never hold live values.

| Azure App Setting | GitHub Secret | Notes |
|---|---|---|
| `Stripe__SecretKey` | `STRIPE_SECRET_KEY` | Must be `sk_live_` in Production — startup fails on `sk_test_` |
| `Stripe__PublishableKey` | `STRIPE_PUBLISHABLE_KEY` | Must be `pk_live_` in Production |
| `Stripe__WebhookSecret` | `STRIPE_WEBHOOK_SECRET` | From the Dashboard webhook endpoint (below) |
| `Stripe__PricePlanId` | `STRIPE_PRICE_PLAN_ID` | Must match the seeded `SubscriptionPlans.StripePriceId`; drift is logged as an error at startup |
| `Features__CheckoutEnabled` | — | Kill switch, see below |

## Webhook endpoint registration (manual, Dashboard)

Register in the Stripe Dashboard (live mode) → Developers → Webhooks:

- URL: `https://<api-host>/api/subscription/webhook`
- Events: `checkout.session.completed`, `customer.subscription.created`,
  `customer.subscription.updated`, `customer.subscription.deleted`,
  `invoice.payment_succeeded`, `invoice.payment_failed`
- Copy the signing secret into the `STRIPE_WEBHOOK_SECRET` GitHub secret and redeploy (or set
  the Azure App Setting directly).

## Customer portal (manual, Dashboard)

Settings → Billing → Customer portal: enable payment-method updates, invoice history, and
cancellation. The API's `POST /api/subscription/portal` uses the default portal configuration —
it 400s if none is saved.

## Kill switch

Set Azure App Setting `Features__CheckoutEnabled = false` (App Service restarts automatically).
New checkouts return 503 with a friendly message; existing subscriptions, webhooks, portal, and
cancellation keep working. Remove the setting (or set `true`) to re-enable.

## Launch checklist

1. Rotate the live secret key and webhook signing secret (they sat on developer disks; the
   rotation TODO in `appsettings.Production.json` tracks this). Update the GitHub secrets.
2. Sanitize local `appsettings.json` files: base file must hold test/placeholder values only.
3. Verify `STRIPE_PRICE_PLAN_ID` is the live $5.00/month price and matches the seeded plan row
   (startup logs an error on drift).
4. Register the webhook endpoint (above) and set the new signing secret.
5. Configure the customer portal (above).
6. Deploy, then POST a garbage-signature request to the webhook URL and confirm a Sentry event
   arrives; create a Sentry alert rule on error-level events from the API if not already present.
7. Kill-switch drill: set `Features__CheckoutEnabled=false`, confirm checkout 503s, revert.
8. End-to-end smoke test with a real card: subscribe ($5) → success page activates → enable a
   sync → cancel (verify "active until" messaging and `cancel_at_period_end` in the Dashboard)
   → portal card update → wait for period end or delete the subscription in the Dashboard and
   confirm sync configs auto-disable.

## Incident playbook

- **Webhook failures spiking** (Sentry: "Failed to process webhook event"): check DB health
  first. Stripe redelivers on 500 for up to 3 days and the event claim is released on failure,
  so transient outages self-heal. The internal retry queue (`WebhookRetries`) is a second net.
- **Signature verification failures** (Sentry: "signature verification failed"): either an
  attack (ignore) or a rotated-but-not-deployed webhook secret (fix the App Setting).
- **User paid but not active**: `POST /api/subscription/checkout/complete` from the success
  page usually fixes it instantly; the hourly reconciliation job (`StripeReconciliation`)
  creates any missing rows from Stripe's active-subscription list. To force it, check the
  Stripe Dashboard for the subscription and its `userId` metadata.
- **Disable payments entirely**: kill switch (above).

## Event → state mapping

| Stripe event | Local effect |
|---|---|
| `customer.subscription.created` / `updated` | Upsert row (status, period dates, `CancelAtPeriodEnd`); entitled→syncs re-enabled, inactive→syncs disabled |
| `customer.subscription.deleted` | Status `canceled`, `CanceledAt` stamped, sync configs disabled |
| `invoice.payment_failed` | Status `past_due`, sync configs disabled |
| `invoice.payment_succeeded` | Status `active`, previously disabled syncs re-enabled |
| `checkout.session.completed` | Log only (subscription events carry the state) |

`active` and `trialing` are the entitled statuses; `incomplete_expired` maps to `canceled`.
