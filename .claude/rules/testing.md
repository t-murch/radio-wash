---
paths:
  - "**/*.test.*"
  - "**/*.spec.*"
  - "**/tests/**"
  - "**/*Tests.cs"
---

# Testing Rules

When writing tests, load the ce:writing-tests skill for general patterns.

## Flaky Tests

When fixing flaky tests, load the ce:fixing-flaky-tests skill.

| Symptom | Likely Cause |
|---------|--------------|
| Passes alone, fails in suite | Shared state |
| Random timing failures | Race condition |
| Works locally, fails in CI | Environment difference |

## Project Test Commands

```bash
# Frontend (Vitest)
pnpm test              # Run all web tests
pnpm test:watch        # Watch mode
pnpm test:ui           # Vitest UI

# Backend (.NET)
dotnet test            # Run all API tests
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# E2E
pnpm test:e2e          # Playwright tests
```
