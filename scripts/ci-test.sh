#!/usr/bin/env bash
set -euo pipefail

SHARD="${2:-}"

# 1. Start supabase services
docker compose -f docker-compose.ci.yml up -d

# 2. Wait for db to be ready
echo "⏳ Waiting for Supabase..."
for i in {1..30}; do
  if nc -z localhost 54321; then
    echo "✅ Supabase ready"
    break
  fi
  sleep 2
done

# 3. Run migrations
npm run migrate

# 4. Run tests (shard if passed)
if [ "$1" = "shard" ] && [ -n "$SHARD" ]; then
  echo "🔀 Running tests for shard $SHARD"
  npm run test -- --shard=$SHARD
else
  echo "🧪 Running full test suite"
  npm run test
fi

# 5. Shutdown stack
docker compose -f docker-compose.ci.yml down -v
