---
paths:
  - "api.Tests/**/*.cs"
  - "api/**/*Tests.cs"
---

# .NET API Testing (xUnit)

Extends the universal testing rules with .NET-specific patterns.

## Commands

```bash
dotnet test                                    # Run all tests
dotnet test --filter "Category=Unit"           # Unit tests only
dotnet test --filter "Category=Integration"    # Integration tests only
dotnet test -v detailed                        # Verbose output
```

## Test Organization

```
api.Tests/
├── Unit/                    # Fast, isolated tests
│   └── Controllers/
│       └── AuthControllerTests.cs
└── Integration/             # Tests with real dependencies
    ├── Authentication/
    │   └── LocalSupabaseAuthenticationTests.cs
    └── TestHelpers/
        ├── JwtTestHelper.cs
        └── LocalSupabaseTestFixture.cs
```

## Integration Test Pattern

Use `WebApplicationFactory` for integration tests:

```csharp
public class MyIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public MyIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetEndpoint_ReturnsSuccess()
    {
        var response = await _client.GetAsync("/api/endpoint");
        response.EnsureSuccessStatusCode();
    }
}
```

## Mocking External Services

For Supabase/external APIs, use the test fixtures in `TestHelpers/`:

```csharp
public class MyTests : IClassFixture<LocalSupabaseWebApplicationFactory>
{
    // Uses real local Supabase for maximum fidelity
}
```
