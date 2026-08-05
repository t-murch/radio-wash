using Microsoft.EntityFrameworkCore;

namespace RadioWash.Api.Infrastructure.Data;

public static class DbUpdateExceptionExtensions
{
  /// <summary>
  /// True when the failed save was rejected by a unique constraint — the signal callers use
  /// to treat an insert as "lost a benign race" rather than a real error. A PostgresException
  /// is classified by SqlState alone (23505) and never falls through to message matching, so
  /// other Postgres failures whose text happens to mention "unique" are not swallowed.
  /// </summary>
  public static bool IsUniqueConstraintViolation(this DbUpdateException ex)
  {
    if (ex.InnerException is Npgsql.PostgresException pgEx)
    {
      return pgEx.SqlState == "23505";
    }

    // Fallback for the EF InMemory provider used in unit tests, which surfaces unique
    // index violations as a plain exception message.
    return ex.InnerException?.Message?.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
        || ex.Message.Contains("same key", StringComparison.OrdinalIgnoreCase);
  }
}
