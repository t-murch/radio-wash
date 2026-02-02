---
paths:
  - "**/*"
---

# Debugging

When investigating bugs or unexpected behavior, load the ce:systematic-debugging skill.

## Four-Phase Approach

1. **Reproduce** - Confirm the issue with a minimal reproduction
2. **Trace** - Follow the code path, check logs, add instrumentation
3. **Identify** - Find the root cause, not just symptoms
4. **Verify** - Confirm the fix resolves the issue without side effects

## Project-Specific Debugging

### Frontend
- Check browser console for errors
- Use React DevTools for component state
- Check Network tab for API failures
- Use `console.log` strategically, remove before commit

### Backend
- Check application logs: `docker compose logs api`
- Use Swagger UI at `/swagger` for API testing
- Check Hangfire dashboard for background job issues
