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
    }

}
