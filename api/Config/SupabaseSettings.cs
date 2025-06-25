namespace RadioWash.Api.Configuration;

public class SupabaseSettings
{
    public const string SectionName = "Supabase";
    
    public string Url { get; set; } = null!;
    public string AnonKey { get; set; } = null!;
    public string ServiceRoleKey { get; set; } = null!;
    public string JwtSecret { get; set; } = null!;
    public string JwtIssuer { get; set; } = null!;
}