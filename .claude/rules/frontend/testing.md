---
paths:
  - "web/**/*.test.ts"
  - "web/**/*.test.tsx"
  - "web/**/*.spec.ts"
  - "web/**/__tests__/**"
---

# Frontend Testing (Vitest + Testing Library)

Extends the universal testing rules with frontend-specific patterns.

## Commands

```bash
pnpm test              # Run all tests
pnpm test:watch        # Watch mode
pnpm test:ui           # Vitest UI
```

## HTTP Mocking with MSW

This project uses MSW for API mocking:

```typescript
import { http, HttpResponse } from 'msw'

export const handlers = [
  http.get('/api/user', () => {
    return HttpResponse.json({ name: 'Test User' })
  })
]
```

## Async Waiting

Always use proper waiting:

```typescript
// Correct
await waitFor(() => expect(element).toBeVisible())

// Incorrect - arbitrary timeout
await sleep(500)
```

## Component Testing

Use Testing Library idioms:

```typescript
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

test('submits form on button click', async () => {
  const user = userEvent.setup()
  render(<MyForm />)

  await user.type(screen.getByLabelText('Email'), 'test@example.com')
  await user.click(screen.getByRole('button', { name: /submit/i }))

  expect(screen.getByText('Success')).toBeInTheDocument()
})
```
