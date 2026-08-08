# Ops runbooks

## Resetting a deployed database for the Apple Music redesign

**Applies to:** any environment whose database was built by the pre-redesign
migration chain — i.e. anything deployed before the Apple Music work.

The 19 migrations that built the old schema were replaced by a single
`InitialAppleMusic` migration. That migration creates every table from scratch,
so against a database that still holds the old tables it fails immediately:

```
42P07: relation "DataProtectionKeys" already exists
```

`Program.cs` rethrows after `Database.Migrate()`, so this is not a warning — the
application will not start.

### Procedure

1. **Stop the API** (scale to zero, or stop the container) so nothing writes
   during the reset.
2. Open the Supabase dashboard for the target project → **SQL Editor**.
3. Paste and run [`reset-hosted-db.sql`](./reset-hosted-db.sql).
   It prints a row count per table before dropping anything — read that output.
   If it lists rows you want to keep, `ROLLBACK` rather than `COMMIT`.
4. **Start the API.** Startup applies `InitialAppleMusic`, recreates the
   `auth.users` trigger, and seeds the Sync Plan.
5. Verify:

   ```sql
   SELECT "MigrationId" FROM "__EFMigrationsHistory";
   -- expect exactly: 20260807235847_InitialAppleMusic

   SELECT column_name, column_default
   FROM information_schema.columns
   WHERE table_name = 'CleanPlaylistJobs'
     AND column_name IN ('Provider', 'TargetProvider');
   -- expect both: 'apple_music'::character varying
   ```

### What it destroys

All RadioWash application data: users, jobs, track mappings, sync configs and
history, subscriptions, stored music tokens, and the Stripe webhook idempotency
log.

Supabase's own schemas are untouched, so accounts in `auth.users` survive. Their
RadioWash profile rows do not — the first request from an existing account
recreates the profile through the trigger.

**This is only appropriate where the data is disposable.** For a database with
real users or live Stripe subscriptions, the alternative is to reconcile in
place: rewrite `__EFMigrationsHistory` to hold only `InitialAppleMusic` and patch
whatever schema drift exists. That is a bespoke script, not this one.

### Note on encrypted tokens

Stored Apple Music tokens are encrypted with ASP.NET Data Protection keys kept in
`DataProtectionKeys`. Dropping that table makes any surviving token
undecryptable. Moot here, since the tokens are dropped in the same statement —
but it matters if you adapt this script for a database whose rows you keep.
