using Npgsql;

namespace RadioWash.Api.Test.Integration;

/// <summary>
/// Test direct database connectivity
/// </summary>
public class DatabaseConnectionTest
{
    [Fact]
    public void TestDatabaseConnection()
    {
        var connectionString = "Host=localhost;Port=15432;Database=radiowash_test;Username=postgres;Password=postgres";
        
        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            
            // Test basic query
            using var cmd = new NpgsqlCommand("SELECT 1 as test_value;", connection);
            var result = cmd.ExecuteScalar();
            
            System.Console.WriteLine($"Database connection successful. Test value: {result}");
            Assert.Equal(1, result);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Database connection failed: {ex.Message}");
            throw;
        }
    }
    
    [Fact]
    public void TestCreateAuthSchema()
    {
        var connectionString = "Host=localhost;Port=15432;Database=radiowash_test;Username=postgres;Password=postgres";
        
        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            
            // Create auth schema
            using var cmd1 = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth;", connection);
            cmd1.ExecuteNonQuery();
            System.Console.WriteLine("Auth schema created successfully");
            
            // Create minimal auth.users table
            using var cmd2 = new NpgsqlCommand(@"
                CREATE TABLE IF NOT EXISTS auth.users (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    email TEXT UNIQUE,
                    raw_user_meta_data JSONB,
                    created_at TIMESTAMPTZ DEFAULT NOW(),
                    updated_at TIMESTAMPTZ DEFAULT NOW()
                );
            ", connection);
            cmd2.ExecuteNonQuery();
            System.Console.WriteLine("Auth users table created successfully");
            
            Assert.True(true); // Test passed
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Auth schema creation failed: {ex.Message}");
            throw;
        }
    }
}