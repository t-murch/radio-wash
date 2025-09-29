namespace RadioWash.Api.Models.Domain;

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int PriceInCents { get; set; }
    public string BillingPeriod { get; set; } = null!; // 'monthly', 'yearly'
    public int? MaxPlaylists { get; set; } // NULL for unlimited
    public int? MaxTracksPerPlaylist { get; set; } // NULL for unlimited
    public string Features { get; set; } = "{}"; // JSON features
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<UserSubscription> UserSubscriptions { get; set; } = new List<UserSubscription>();
}