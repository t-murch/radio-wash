using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Data;

public class RadioWashDbContext : DbContext
{
  public RadioWashDbContext(DbContextOptions<RadioWashDbContext> options) : base(options)
  {
  }

  public DbSet<User> Users { get; set; } = null!;
  public DbSet<UserToken> UserTokens { get; set; } = null!;
  public DbSet<CleanPlaylistJob> CleanPlaylistJobs { get; set; } = null!;
  public DbSet<TrackMapping> TrackMappings { get; set; } = null!;

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // User configuration
    modelBuilder.Entity<User>(entity =>
    {
      entity.HasKey(e => e.Id);
      entity.HasIndex(e => e.SpotifyId).IsUnique();
      entity.Property(e => e.SpotifyId).IsRequired();
      entity.Property(e => e.DisplayName).IsRequired();
      entity.Property(e => e.Email).IsRequired();
      entity.Property(e => e.CreatedAt).IsRequired();
      entity.Property(e => e.UpdatedAt).IsRequired();

      entity.HasOne(e => e.Token)
              .WithOne(e => e.User)
              .HasForeignKey<UserToken>(e => e.UserId)
              .OnDelete(DeleteBehavior.Cascade);

      entity.HasMany(e => e.Jobs)
              .WithOne(e => e.User)
              .HasForeignKey(e => e.UserId)
              .OnDelete(DeleteBehavior.Cascade);
    });

    // UserToken configuration
    modelBuilder.Entity<UserToken>(entity =>
    {
      entity.HasKey(e => e.Id);
      entity.HasIndex(e => e.UserId).IsUnique();
      entity.Property(e => e.AccessToken).IsRequired();
      entity.Property(e => e.RefreshToken).IsRequired();
      entity.Property(e => e.ExpiresAt).IsRequired();
      entity.Property(e => e.CreatedAt).IsRequired();
      entity.Property(e => e.UpdatedAt).IsRequired();
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
