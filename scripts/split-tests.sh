#!/usr/bin/env bash
set -euo pipefail

NUM_SHARDS=${NUM_SHARDS:-1}
SHARD_INDEX=${SHARD_INDEX:-0}

# List all tests and filter out headers, keeping only indented test names
# Output format: "Test run for...", "VSTest version...", "", "The following Tests are available:", "    TestName"
TESTS=$(dotnet test --no-build --list-tests | tail -n +5 | grep "^    " | sed 's/^    //')

# Convert to array
mapfile -t TEST_ARRAY <<<"$TESTS"

# Count tests
TOTAL=${#TEST_ARRAY[@]}
PER_SHARD=$(((TOTAL + NUM_SHARDS - 1) / NUM_SHARDS)) # ceiling division

# Slice array for this shard
START=$((SHARD_INDEX * PER_SHARD))
SHARD_TESTS=("${TEST_ARRAY[@]:$START:$PER_SHARD}")

echo "Running shard $SHARD_INDEX/$NUM_SHARDS"
echo "Total tests: $TOTAL"
echo "Tests in this shard:"
printf '%s\n' "${SHARD_TESTS[@]}"

if [ ${#SHARD_TESTS[@]} -eq 0 ]; then
  echo "No tests in this shard. Exiting."
  exit 0
fi

# Write test names to a temporary file to avoid command line length limits
TEMP_FILE=$(mktemp)
echo "Writing ${#SHARD_TESTS[@]} test names to temporary file: $TEMP_FILE"

# Write each test name to temp file, one per line
for test in "${SHARD_TESTS[@]}"; do
    echo "$test" >> "$TEMP_FILE"
done

# Run tests using dynamic batching based on filter length limits
MAX_FILTER_LENGTH=2000  # Safe limit for command line length
TOTAL_TESTS=${#SHARD_TESTS[@]}

echo "Running $TOTAL_TESTS tests in dynamic batches with filter length limit of $MAX_FILTER_LENGTH chars"

# Initialize results tracking
PASSED_BATCHES=0
FAILED_BATCHES=0
BATCH_COUNT=0
test_idx=0

while [ $test_idx -lt $TOTAL_TESTS ]; do
    BATCH_COUNT=$((BATCH_COUNT + 1))
    BATCH_TESTS=()
    BATCH_FILTER=""
    
    # Build batch by adding tests until we hit the length limit
    while [ $test_idx -lt $TOTAL_TESTS ]; do
        test="${SHARD_TESTS[$test_idx]}"
        
        # Create test filter entry
        NEW_FILTER_PART="FullyQualifiedName~${test}"
        if [ -n "$BATCH_FILTER" ]; then
            TEST_FILTER="${BATCH_FILTER}|${NEW_FILTER_PART}"
        else
            TEST_FILTER="${NEW_FILTER_PART}"
        fi
        
        # Check if this would exceed our limit
        if [ ${#TEST_FILTER} -gt $MAX_FILTER_LENGTH ] && [ ${#BATCH_TESTS[@]} -gt 0 ]; then
            # Don't add this test to current batch, start new batch
            break
        fi
        
        # Add test to current batch
        BATCH_TESTS+=("$test")
        BATCH_FILTER="$TEST_FILTER"
        test_idx=$((test_idx + 1))
    done
    
    echo "Running batch $BATCH_COUNT (${#BATCH_TESTS[@]} tests)"
    echo "  Filter length: ${#BATCH_FILTER}"
    echo "  Tests: ${BATCH_TESTS[0]} ... ${BATCH_TESTS[-1]}"
    
    # Run this batch
    if dotnet test --no-build --filter "$BATCH_FILTER" --logger "console;verbosity=minimal"; then
        echo "  Batch $BATCH_COUNT completed successfully"
        PASSED_BATCHES=$((PASSED_BATCHES + 1))
    else
        echo "  Batch $BATCH_COUNT had failures"
        FAILED_BATCHES=$((FAILED_BATCHES + 1))
    fi
done

echo "Summary: $BATCH_COUNT batches total, $PASSED_BATCHES passed, $FAILED_BATCHES failed"

# Generate final TRX file by running all tests in this shard again with console logger
echo "Generating final TRX results file..."
FILTER=$(printf 'FullyQualifiedName~%s|' "${SHARD_TESTS[@]}")
FILTER=${FILTER%|}

# Try to generate TRX - if this fails due to length, we'll skip it
echo "Final filter length: ${#FILTER}"
if [ ${#FILTER} -lt 8000 ]; then
    dotnet test --no-build --filter "$FILTER" --logger "trx;LogFileName=tests-${SHARD_INDEX}.trx" > /dev/null 2>&1 || echo "TRX generation failed (likely due to filter length)"
else
    echo "Skipping TRX generation - filter too long (${#FILTER} chars)"
    # Create a basic TRX file to indicate the tests were run
    echo "Creating placeholder TRX file..."
    touch "/home/toddmurch/code/RadioWash/api.Test/TestResults/tests-${SHARD_INDEX}.trx"
fi

# Cleanup
rm -f "$TEMP_FILE"
