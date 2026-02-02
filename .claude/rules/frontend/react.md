---
paths:
  - "web/**/*.tsx"
  - "web/**/*.jsx"
---

# React Patterns (Next.js 15)

## Component Structure

- Prefer function components with hooks
- Keep components focused on one responsibility
- Extract custom hooks for reusable logic
- Use Server Components by default, Client Components when needed

## Server vs Client Components

```typescript
// Server Component (default) - no "use client" directive
export default async function Page() {
  const data = await fetchData() // Can fetch directly
  return <div>{data.title}</div>
}

// Client Component - needs interactivity
'use client'
export function Counter() {
  const [count, setCount] = useState(0)
  return <button onClick={() => setCount(c => c + 1)}>{count}</button>
}
```

## Data Fetching

Use TanStack Query for client-side data:

```typescript
const { data, isLoading, error } = useQuery({
  queryKey: ['user', userId],
  queryFn: () => fetchUser(userId),
})
```

## Styling

Use the semantic color system defined in CLAUDE.md:

```tsx
// Correct - semantic colors
<span className="text-success">Completed</span>
<button className="bg-brand hover:bg-brand-hover">Action</button>

// Avoid - hardcoded colors
<span className="text-green-500">Completed</span>
```
