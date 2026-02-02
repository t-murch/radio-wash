---
paths:
  - "**/*.ts"
  - "**/*.tsx"
  - "**/*.cs"
---

# Error Handling

When designing error handling, load the ce:handling-errors skill.

## Key Principles

- Never swallow errors silently
- Preserve error context when re-throwing
- Log errors once at the appropriate boundary
- Use typed errors where possible

## Frontend (TypeScript)

```typescript
// Prefer Result patterns or explicit error handling
try {
  const result = await fetchData();
  return { data: result, error: null };
} catch (error) {
  console.error('Failed to fetch data:', error);
  return { data: null, error: error instanceof Error ? error : new Error(String(error)) };
}
```

## Backend (.NET)

```csharp
// Use Problem Details for API errors
return Problem(
    title: "Validation Error",
    detail: "The request contains invalid data",
    statusCode: StatusCodes.Status400BadRequest
);
```
