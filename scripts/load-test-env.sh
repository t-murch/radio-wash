#!/bin/bash
# Script to load test environment variables for integration tests

# Check if .env.test exists
if [ -f ".env.test" ]; then
    echo "Loading test environment variables from .env.test..."
    export $(grep -v '^#' .env.test | xargs)
    echo "Test environment variables loaded."
else
    echo "Warning: .env.test file not found."
    echo "Please create .env.test from .env.test.example and add your test Supabase values."
    echo "Using fallback values for integration tests."
fi

echo ""
echo "Running integration tests..."
dotnet test --filter "FullyQualifiedName~Integration" --logger console --verbosity minimal