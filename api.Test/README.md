# Sharded Integration Test Runner

This document explains the design and usage of the custom script for running integration tests in parallel across multiple shards.  
The goal is to **reduce total CI time** by splitting the full test suite into evenly sized chunks that can be executed in parallel (e.g., across GitHub Actions matrix jobs).

---

## Problem

- Our integration test suite has grown large enough that a single `dotnet test` run can take significant time.
- CI/CD pipelines support parallel jobs, but `dotnet test` by itself cannot natively split tests into shards.
- We need a way to:
  1. List all test cases in the project.
  2. Split them evenly across `N` shards.
  3. Run only the assigned shard on each job.

---

## Design

1. **Test Discovery**

   - `dotnet test --no-build --list-tests` lists all fully qualified test names.
   - The script captures this output, skipping the header line.

2. **Sharding**

   - Tests are loaded into a bash array.
   - Total tests = `TOTAL`.
   - Each shard gets `ceil(TOTAL / NUM_SHARDS)` tests.
   - Shards are addressed with an environment variable `SHARD_INDEX` (0-based).

   Example:  
    TOTAL = 120 tests
   NUM_SHARDS = 3 → 40 tests per shard
   SHARD_INDEX = 0 → tests 0–39
   SHARD_INDEX = 1 → tests 40–79
   SHARD_INDEX = 2 → tests 80–119

3. **Filtering Tests**

   - Each shard builds a filter expression of the form:
     ```
     FullyQualifiedName~Test1|FullyQualifiedName~Test2|...
     ```
   - This filter is passed to `dotnet test --filter`.

4. **Per-Shard Logging**
   - Each shard produces its own TRX results file:
     ```
     tests-<SHARD_INDEX>.trx
     ```

---

## Script

Located at: `scripts/split-tests.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail

NUM_SHARDS=${NUM_SHARDS:-1}
SHARD_INDEX=${SHARD_INDEX:-0}

TESTS=$(dotnet test --no-build --list-tests | tail -n +2)
mapfile -t TEST_ARRAY <<<"$TESTS"

TOTAL=${#TEST_ARRAY[@]}
PER_SHARD=$(((TOTAL + NUM_SHARDS - 1) / NUM_SHARDS))

START=$((SHARD_INDEX * PER_SHARD))
SHARD_TESTS=("${TEST_ARRAY[@]:$START:$PER_SHARD}")

echo "Running shard $SHARD_INDEX/$NUM_SHARDS (total: $TOTAL tests)"
printf '%s\n' "${SHARD_TESTS[@]}"

if [ ${#SHARD_TESTS[@]} -eq 0 ]; then
echo "No tests in this shard. Exiting."
exit 0
fi

FILTER=$(printf 'FullyQualifiedName~%s|' "${SHARD_TESTS[@]}")
FILTER=${FILTER%|}

dotnet test --no-build --filter "$FILTER" --logger "trx;LogFileName=tests-${SHARD_INDEX}.trx"
```
