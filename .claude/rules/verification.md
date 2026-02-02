---
paths:
  - "**/*"
---

# Verification

Before claiming work is complete, load the ce:verification-before-completion skill.

## Always Verify

- [ ] Tests pass (`pnpm test` and `dotnet test`)
- [ ] Linting passes (`pnpm lint` if configured)
- [ ] Build succeeds (`dotnet build` and `nx build web`)
- [ ] Feature works end-to-end manually
- [ ] No console errors in browser

## Before Committing

- [ ] `git diff` shows only intended changes
- [ ] No debug code left (console.log, TODO comments, etc.)
- [ ] No sensitive data in commits
