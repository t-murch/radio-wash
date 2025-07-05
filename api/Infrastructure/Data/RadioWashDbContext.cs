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

    modelBuilder.Entity<User>()
        .HasIndex(u => u.SupabaseId)
        .IsUnique();

    modelBuilder.Entity<CleanPlaylistJob>()
        .HasOne(j => j.User)
        .WithMany(u => u.Jobs)
        .HasForeignKey(j => j.UserId);

    modelBuilder.Entity<TrackMapping>()
        .HasOne(t => t.Job)
        .WithMany(j => j.TrackMappings)
        .HasForeignKey(t => t.JobId);

  }
}
