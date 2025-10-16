using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Data;

public class RadioWashDbContext : DbContext, IDataProtectionKeyContext
{
  public RadioWashDbContext(DbContextOptions<RadioWashDbContext> options) : base(options)
  {
  }

  public DbSet<User> Users { get; set; } = null!;
  public DbSet<UserProviderData> UserProviderData { get; set; } = null!;
  public DbSet<UserMusicToken> UserMusicTokens { get; set; } = null!;
  public DbSet<CleanPlaylistJob> CleanPlaylistJobs { get; set; } = null!;
  public DbSet<TrackMapping> TrackMappings { get; set; } = null!;

  // Subscription entities
  public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; } = null!;
  public DbSet<UserSubscription> UserSubscriptions { get; set; } = null!;
  public DbSet<PlaylistSyncConfig> PlaylistSyncConfigs { get; set; } = null!;
  public DbSet<PlaylistSyncHistory> PlaylistSyncHistory { get; set; } = null!;
  public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; set; } = null!;
  public DbSet<WebhookRetry> WebhookRetries { get; set; } = null!;

  // Data Protection Keys
  public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<User>()
        .HasIndex(u => u.SupabaseId)
        .IsUnique();

    modelBuilder.Entity<CleanPlaylistJob>()
        .HasOne(j => j.User)
        .WithMany(u => u.Jobs)
        .HasForeignKey(j => j.UserId);

    modelBuilder.Entity<UserProviderData>()
        .HasOne(upd => upd.User)
        .WithMany(u => u.ProviderData)
        .HasForeignKey(upd => upd.UserId);

    modelBuilder.Entity<UserProviderData>()
        .HasIndex(upd => new { upd.Provider, upd.ProviderId })
        .IsUnique();

    modelBuilder.Entity<UserMusicToken>()
        .HasOne(umt => umt.User)
        .WithMany()
        .HasForeignKey(umt => umt.UserId);

    modelBuilder.Entity<UserMusicToken>()
        .HasIndex(umt => new { umt.UserId, umt.Provider })
        .IsUnique();

    // Subscription Plan configuration
    modelBuilder.Entity<SubscriptionPlan>()
        .HasIndex(sp => sp.Name)
        .IsUnique();

    // User Subscription configuration
    modelBuilder.Entity<UserSubscription>()
        .HasOne(us => us.User)
        .WithMany()
        .HasForeignKey(us => us.UserId);

    modelBuilder.Entity<UserSubscription>()
        .HasOne(us => us.Plan)
        .WithMany(sp => sp.UserSubscriptions)
        .HasForeignKey(us => us.PlanId);

    modelBuilder.Entity<UserSubscription>()
        .HasIndex(us => us.UserId);

    modelBuilder.Entity<UserSubscription>()
        .HasIndex(us => us.StripeSubscriptionId)
        .IsUnique()
        .HasFilter("\"StripeSubscriptionId\" IS NOT NULL");

    modelBuilder.Entity<UserSubscription>()
        .HasIndex(us => us.Status);

    // Playlist Sync Config configuration
    modelBuilder.Entity<PlaylistSyncConfig>()
        .HasOne(psc => psc.User)
        .WithMany()
        .HasForeignKey(psc => psc.UserId);

    modelBuilder.Entity<PlaylistSyncConfig>()
        .HasOne(psc => psc.OriginalJob)
        .WithMany()
        .HasForeignKey(psc => psc.OriginalJobId);

    modelBuilder.Entity<PlaylistSyncConfig>()
        .HasIndex(psc => psc.UserId);

    modelBuilder.Entity<PlaylistSyncConfig>()
        .HasIndex(psc => psc.NextScheduledSync);

    modelBuilder.Entity<PlaylistSyncConfig>()
        .HasIndex(psc => new { psc.UserId, psc.OriginalJobId })
        .IsUnique();

    // Playlist Sync History configuration
    modelBuilder.Entity<PlaylistSyncHistory>()
        .HasOne(psh => psh.SyncConfig)
        .WithMany(psc => psc.SyncHistory)
        .HasForeignKey(psh => psh.SyncConfigId);

    modelBuilder.Entity<PlaylistSyncHistory>()
        .HasIndex(psh => psh.SyncConfigId);

    modelBuilder.Entity<PlaylistSyncHistory>()
        .HasIndex(psh => psh.StartedAt);

    // Processed Webhook Event configuration
    modelBuilder.Entity<ProcessedWebhookEvent>()
        .HasIndex(pwe => pwe.EventId)
        .IsUnique();

    modelBuilder.Entity<ProcessedWebhookEvent>()
        .HasIndex(pwe => pwe.ProcessedAt);

    // Webhook Retry configuration
    modelBuilder.Entity<WebhookRetry>()
        .HasIndex(wr => wr.EventId);

    modelBuilder.Entity<WebhookRetry>()
        .HasIndex(wr => wr.NextRetryAt);

    modelBuilder.Entity<WebhookRetry>()
        .HasIndex(wr => new { wr.Status, wr.NextRetryAt });
  }

}
