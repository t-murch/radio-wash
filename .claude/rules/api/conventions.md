---
paths:
  - "api/**/*.cs"
---

# .NET API Conventions

## Project Structure

```
api/
├── Controllers/     # HTTP endpoints
├── Models/          # Domain models and DTOs
├── Infrastructure/  # Data access, external services
├── Middleware/      # Request/response pipeline
├── Hubs/           # SignalR real-time hubs
├── Config/         # Configuration classes
└── Program.cs      # Application entry point
```

## Controller Patterns

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MyController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MyResponse>> Get()
    {
        // Return typed responses
        return Ok(new MyResponse { ... });
    }

    [HttpPost]
    public async Task<ActionResult<MyResponse>> Create(MyRequest request)
    {
        // Validate and create
        return CreatedAtAction(nameof(Get), new { id }, response);
    }
}
```

## Error Responses

Use Problem Details for consistent error responses:

```csharp
return Problem(
    title: "Resource Not Found",
    detail: $"User with ID {id} was not found",
    statusCode: StatusCodes.Status404NotFound
);
```

## Authentication

JWT authentication is configured via JWKS from Supabase. Use `[Authorize]` attribute on controllers/actions requiring auth.
