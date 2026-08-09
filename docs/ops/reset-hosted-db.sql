-- Reset the hosted RadioWash database for the Apple Music redesign.
--
-- WHY THIS IS NEEDED
-- The 19 migrations that built the old schema were squashed into a single
-- InitialAppleMusic migration. That migration CREATEs every table from scratch,
-- so against a database that still has the old tables it fails immediately with
--   42P07: relation "DataProtectionKeys" already exists
-- and because Program.cs rethrows after Database.Migrate(), the app does not start.
--
-- Running this script drops everything the API owns. The next deploy's startup
-- migration then rebuilds the schema cleanly and reseeds the subscription plan.
--
-- SCOPE — read before running
--   * Deletes ALL RadioWash application data: users, jobs, track mappings, sync
--     configs and history, subscriptions, stored music tokens, and the Stripe
--     webhook idempotency log.
--   * ALSO deletes every account in auth.users. Profile rows are created only by
--     the AFTER INSERT trigger on auth.users — an account that survives a reset
--     with its profile row dropped would be stranded behind "User not found"
--     forever, because an insert trigger never re-fires for an existing row.
--     Deleting the accounts means everyone signs up again and the trigger
--     provisions them cleanly. (Verified the hard way in local dev, Aug 2026.)
--   * Does NOT touch Supabase's other schemas (storage, realtime, extensions) or
--     drop the database itself, so connection details stay valid.
--
-- Run it in the Supabase dashboard SQL editor for the target project, with the API
-- stopped or scaled to zero so nothing writes mid-reset.

BEGIN;

-- 1. Report what is about to be destroyed. Read this output before committing.
--    If it shows rows you care about, ROLLBACK instead of COMMIT.
DO $$
DECLARE
  r RECORD;
  n BIGINT;
BEGIN
  RAISE NOTICE '--- rows about to be dropped ---';
  FOR r IN
    SELECT tablename FROM pg_tables
    WHERE schemaname = 'public'
    ORDER BY tablename
  LOOP
    EXECUTE format('SELECT count(*) FROM public.%I', r.tablename) INTO n;
    RAISE NOTICE '% : % row(s)', rpad(r.tablename, 34), n;
  END LOOP;
END $$;

-- 2. The auth.users trigger references public."Users". Drop it first so the table
--    drop cannot fail on the dependency, and so a signup arriving mid-reset cannot
--    insert into a table that is disappearing. InitialAppleMusic recreates both.
DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
DROP FUNCTION IF EXISTS public.handle_new_auth_user();

-- 2b. Remove the accounts themselves. Supabase cascades from auth.users to its own
--     dependent tables (identities, sessions, refresh tokens). Without this, any
--     account that existed before the reset can never regain a profile row — the
--     trigger only fires on INSERT, and their row already exists.
DO $$
DECLARE
  n BIGINT;
BEGIN
  SELECT count(*) INTO n FROM auth.users;
  RAISE NOTICE 'auth.users accounts about to be deleted: %', n;
END $$;
DELETE FROM auth.users;

-- 3. Drop every table the API owns, plus EF's migration ledger. CASCADE clears the
--    foreign keys between them; order does not matter because of it.
--
--    "UserTokens" is not in the current model — it is a leftover from an earlier
--    schema that was never cleaned up. Listed so it does not linger.
DROP TABLE IF EXISTS
  public."TrackMappings",
  public."PlaylistSyncHistory",
  public."PlaylistSyncConfigs",
  public."CleanPlaylistJobs",
  public."UserSubscriptions",
  public."SubscriptionPlans",
  public."UserMusicTokens",
  public."UserProviderData",
  public."UserTokens",
  public."Users",
  public."WebhookRetries",
  public."ProcessedWebhookEvents",
  public."DataProtectionKeys",
  public."__EFMigrationsHistory"
CASCADE;

-- 4. Confirm public is empty of application tables. Anything still listed here was
--    not created by the API and is left deliberately untouched.
DO $$
DECLARE
  leftover TEXT;
BEGIN
  SELECT string_agg(tablename, ', ' ORDER BY tablename)
  INTO leftover
  FROM pg_tables
  WHERE schemaname = 'public';

  IF leftover IS NULL THEN
    RAISE NOTICE 'public schema is empty — ready for InitialAppleMusic.';
  ELSE
    RAISE NOTICE 'Remaining non-API tables in public (left alone): %', leftover;
  END IF;
END $$;

COMMIT;

-- AFTERWARDS
--   1. Deploy or restart the API. Startup applies InitialAppleMusic, recreates the
--      auth.users trigger, and seeds the Sync Plan at $5.00.
--   2. Expect exactly one row in __EFMigrationsHistory:
--        SELECT "MigrationId" FROM "__EFMigrationsHistory";
--        -> 20260807235847_InitialAppleMusic
--   3. Sanity-check the provider defaults are Apple, not Spotify:
--        SELECT column_name, column_default
--        FROM information_schema.columns
--        WHERE table_name = 'CleanPlaylistJobs'
--          AND column_name IN ('Provider', 'TargetProvider');
--        -> both 'apple_music'::character varying
--
-- Stored Apple Music tokens are encrypted with ASP.NET Data Protection keys that
-- lived in "DataProtectionKeys". Dropping that table makes any surviving token
-- undecryptable — which is moot here because the tokens are dropped in the same
-- statement, but worth knowing before adapting this script to a database whose
-- rows you intend to keep.
