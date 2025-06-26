using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Data;

public class RadioWashDbContext : DbContext
{
  public RadioWashDbContext(DbContextOptions<RadioWashDbContext> options) : base(options)
  {
  }

  public DbSet<User> Users { get; set; } = null!;
  public DbSet<CleanPlaylistJob> CleanPlaylistJobs { get; set; } = null!;
  public DbSet<TrackMapping> TrackMappings { get; set; } = null!;

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // User configuration
    modelBuilder.Entity<User>(entity =>
    {
      entity.HasKey(e => e.Id);
      entity.HasIndex(e => e.SupabaseUserId).IsUnique();
      entity.HasIndex(e => e.SpotifyId).IsUnique();
      entity.Property(e => e.SupabaseUserId).IsRequired();
      entity.Property(e => e.SpotifyId).IsRequired();
      entity.Property(e => e.EncryptedSpotifyAccessToken).IsRequired();
      entity.Property(e => e.EncryptedSpotifyRefreshToken).IsRequired();
      entity.Property(e => e.SpotifyTokenExpiresAt).IsRequired();
      entity.Property(e => e.CreatedAt).IsRequired();
      entity.Property(e => e.UpdatedAt).IsRequired();

      entity.HasMany(e => e.Jobs)
              .WithOne(e => e.User)
              .HasForeignKey(e => e.UserId)
              .OnDelete(DeleteBehavior.Cascade);
    });


    // CleanPlaylistJob configuration
    modelBuilder.Entity<CleanPlaylistJob>(entity =>
    {
      entity.HasKey(e => e.Id);
      entity.HasIndex(e => e.UserId);
      entity.Property(e => e.SourcePlaylistId).IsRequired();
      entity.Property(e => e.SourcePlaylistName).IsRequired();
      entity.Property(e => e.Status).IsRequired();
      entity.Property(e => e.CreatedAt).IsRequired();
      entity.Property(e => e.UpdatedAt).IsRequired();

      entity.HasMany(e => e.TrackMappings)
              .WithOne(e => e.Job)
              .HasForeignKey(e => e.JobId)
              .OnDelete(DeleteBehavior.Cascade);
    });

    // TrackMapping configuration
    modelBuilder.Entity<TrackMapping>(entity =>
    {
      entity.HasKey(e => e.Id);
      entity.HasIndex(e => e.JobId);
      entity.Property(e => e.SourceTrackId).IsRequired();
      entity.Property(e => e.SourceTrackName).IsRequired();
      entity.Property(e => e.SourceArtistName).IsRequired();
      entity.Property(e => e.IsExplicit).IsRequired();
      entity.Property(e => e.HasCleanMatch).IsRequired();
      entity.Property(e => e.CreatedAt).IsRequired();
    });
  }
}
