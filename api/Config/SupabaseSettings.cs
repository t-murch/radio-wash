namespace RadioWash.Api.Configuration;

public class SupabaseSettings
{
    public const string SectionName = "Supabase";
    
    public string Url { get; set; } = null!;
    public string AnonKey { get; set; } = null!;
    public string SecretKey { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
}