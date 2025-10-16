using Microsoft.EntityFrameworkCore;
using RadioWash.Api.Infrastructure.Repositories;
using RadioWash.Api.Models.Domain;
using RadioWash.Api.Tests.Unit.Infrastructure;

namespace RadioWash.Api.Tests.Unit.Infrastructure.Repositories;

/// <summary>
/// Unit tests for CleanPlaylistJobRepository
/// Tests job lifecycle management, progress tracking, and status updates
/// </summary>
public class CleanPlaylistJobRepositoryTests : RepositoryTestBase
{
  private readonly CleanPlaylistJobRepository _jobRepository;

  public CleanPlaylistJobRepositoryTests()
  {
    _jobRepository = new CleanPlaylistJobRepository(_context);
  }

  [Fact]
  public async Task GetByIdAsync_WithExistingJob_ReturnsJobWithUserAndTrackMappings()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id, "playlist_123", "Clean Test Playlist");
    await SeedAsync(job);

    var trackMapping = CreateTestTrackMapping(job.Id, "track_123", "clean_track_123");
    await SeedAsync(trackMapping);

    // Act
    var result = await _jobRepository.GetByIdWithDetailsAsync(job.Id);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(job.Id, result.Id);
    Assert.Equal("playlist_123", result.SourcePlaylistId);
    Assert.Equal("Clean Test Playlist", result.TargetPlaylistName);
    Assert.Equal("Pending", result.Status);

    // Verify User is included
    Assert.NotNull(result.User);
    Assert.Equal(user.Id, result.User.Id);
    Assert.Equal(user.SupabaseId, result.User.SupabaseId);

    // Verify TrackMappings are included
    Assert.Single(result.TrackMappings);
    Assert.Equal("track_123", result.TrackMappings.First().SourceTrackId);
  }

  [Fact]
  public async Task GetByIdAsync_WithNonExistentJob_ReturnsNull()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    await SeedAsync(job);

    // Act
    var result = await _jobRepository.GetByIdAsync(999);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task CreateAsync_WithValidJob_CreatesJobSuccessfully()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(
        user.Id,
        "new_playlist_456",
        "New Clean Playlist");

    // Act
    var result = await _jobRepository.CreateAsync(job);

    // Assert
    Assert.NotNull(result);
    Assert.True(result.Id > 0);
    Assert.Equal(user.Id, result.UserId);
    Assert.Equal("new_playlist_456", result.SourcePlaylistId);
    Assert.Equal("New Clean Playlist", result.TargetPlaylistName);
    Assert.Equal("Pending", result.Status);

    // Verify it was saved to database
    var savedJob = await _context.CleanPlaylistJobs.FindAsync(result.Id);
    Assert.NotNull(savedJob);
    Assert.Equal("new_playlist_456", savedJob.SourcePlaylistId);
  }

  [Fact]
  public async Task UpdateAsync_WithValidJob_UpdatesJobAndTimestamp()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    await SeedAsync(job);

    var originalUpdatedAt = job.UpdatedAt;

    // Detach to simulate fresh load
    DetachAllEntities();

    var jobToUpdate = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(jobToUpdate);

    jobToUpdate.Status = "In Progress";
    jobToUpdate.ProcessedTracks = 25;
    jobToUpdate.TotalTracks = 100;
    jobToUpdate.MatchedTracks = 20;

    // Act
    var result = await _jobRepository.UpdateAsync(jobToUpdate);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("In Progress", result.Status);
    Assert.Equal(25, result.ProcessedTracks);
    Assert.Equal(100, result.TotalTracks);
    Assert.Equal(20, result.MatchedTracks);
    Assert.True(result.UpdatedAt > originalUpdatedAt);

    // Verify it was updated in database
    DetachAllEntities();
    var updatedJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal("In Progress", updatedJob.Status);
    Assert.Equal(25, updatedJob.ProcessedTracks);
  }

  [Fact]
  public async Task UpdateStatusAsync_WithExistingJob_UpdatesStatusAndTimestamp()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    job.Status = "Pending";
    await SeedAsync(job);

    var originalUpdatedAt = job.UpdatedAt;

    // Act
    await _jobRepository.UpdateStatusAsync(job.Id, "Completed");

    // Assert
    DetachAllEntities();
    var updatedJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal("Completed", updatedJob.Status);
    Assert.True(updatedJob.UpdatedAt > originalUpdatedAt);
  }

  [Fact]
  public async Task UpdateStatusAsync_WithNonExistentJob_DoesNothing()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    await SeedAsync(job);

    // Act
    await _jobRepository.UpdateStatusAsync(999, "Completed");

    // Assert
    // Should not throw and original job should be unchanged
    DetachAllEntities();
    var originalJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(originalJob);
    Assert.Equal("Pending", originalJob.Status);
  }

  [Fact]
  public async Task UpdateProgressAsync_WithExistingJob_UpdatesProgressAndTimestamp()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    job.ProcessedTracks = 0;
    await SeedAsync(job);

    var originalUpdatedAt = job.UpdatedAt;

    // Act
    await _jobRepository.UpdateProgressAsync(job.Id, 50, "Batch 2");

    // Assert
    DetachAllEntities();
    var updatedJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal(50, updatedJob.ProcessedTracks);
    Assert.Equal("Batch 2", updatedJob.CurrentBatch);
    Assert.True(updatedJob.UpdatedAt > originalUpdatedAt);
  }

  [Fact]
  public async Task UpdateProgressAsync_WithoutCurrentBatch_UpdatesProgressOnly()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    job.ProcessedTracks = 25;
    job.CurrentBatch = "Original Batch";
    await SeedAsync(job);

    // Act
    await _jobRepository.UpdateProgressAsync(job.Id, 75);

    // Assert
    DetachAllEntities();
    var updatedJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal(75, updatedJob.ProcessedTracks);
    Assert.Equal("Original Batch", updatedJob.CurrentBatch); // Should remain unchanged
  }

  [Fact]
  public async Task UpdateProgressAsync_WithEmptyCurrentBatch_UpdatesProgressOnly()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    job.ProcessedTracks = 25;
    job.CurrentBatch = "Original Batch";
    await SeedAsync(job);

    // Act
    await _jobRepository.UpdateProgressAsync(job.Id, 75, "");

    // Assert
    DetachAllEntities();
    var updatedJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal(75, updatedJob.ProcessedTracks);
    Assert.Equal("Original Batch", updatedJob.CurrentBatch); // Should remain unchanged
  }

  [Fact]
  public async Task UpdateProgressAsync_WithNonExistentJob_DoesNothing()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    await SeedAsync(job);

    // Act
    await _jobRepository.UpdateProgressAsync(999, 100, "Final Batch");

    // Assert
    // Should not throw and original job should be unchanged
    DetachAllEntities();
    var originalJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(originalJob);
    Assert.Equal(0, originalJob.ProcessedTracks);
  }

  [Fact]
  public async Task UpdateErrorAsync_WithExistingJob_UpdatesErrorAndStatus()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    job.Status = "In Progress";
    await SeedAsync(job);

    var originalUpdatedAt = job.UpdatedAt;
    var errorMessage = "Spotify API rate limit exceeded";

    // Act
    await _jobRepository.UpdateErrorAsync(job.Id, errorMessage);

    // Assert
    DetachAllEntities();
    var updatedJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(updatedJob);
    Assert.Equal("Failed", updatedJob.Status);
    Assert.Equal(errorMessage, updatedJob.ErrorMessage);
    Assert.True(updatedJob.UpdatedAt > originalUpdatedAt);
  }

  [Fact]
  public async Task UpdateErrorAsync_WithNonExistentJob_DoesNothing()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    await SeedAsync(job);

    // Act
    await _jobRepository.UpdateErrorAsync(999, "Some error");

    // Assert
    // Should not throw and original job should be unchanged
    DetachAllEntities();
    var originalJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(originalJob);
    Assert.Equal("Pending", originalJob.Status);
    Assert.Null(originalJob.ErrorMessage);
  }

  [Fact]
  public async Task SaveChangesAsync_WithPendingChanges_SavesChanges()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    _context.CleanPlaylistJobs.Add(job);

    // Act
    await _jobRepository.SaveChangesAsync();

    // Assert
    Assert.True(job.Id > 0);

    // Verify it was saved
    var savedJob = await _context.CleanPlaylistJobs.FindAsync(job.Id);
    Assert.NotNull(savedJob);
    Assert.Equal(job.SourcePlaylistId, savedJob.SourcePlaylistId);
  }

  [Fact]
  public async Task GetByIdAsync_WithJobWithMultipleTrackMappings_ReturnsAllMappings()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job = CreateTestCleanPlaylistJob(user.Id);
    await SeedAsync(job);

    var mapping1 = CreateTestTrackMapping(job.Id, "track_1", "clean_track_1");
    var mapping2 = CreateTestTrackMapping(job.Id, "track_2", "clean_track_2");
    var mapping3 = CreateTestTrackMapping(job.Id, "track_3", null, isExplicit: true, hasCleanMatch: false);
    await SeedAsync(mapping1, mapping2, mapping3);

    // Act
    var result = await _jobRepository.GetByIdWithDetailsAsync(job.Id);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(3, result.TrackMappings.Count);

    var mappings = result.TrackMappings.ToList();
    Assert.Contains(mappings, m => m.SourceTrackId == "track_1" && m.HasCleanMatch);
    Assert.Contains(mappings, m => m.SourceTrackId == "track_2" && m.HasCleanMatch);
    Assert.Contains(mappings, m => m.SourceTrackId == "track_3" && !m.HasCleanMatch);
  }

  [Fact]
  public async Task CreateAsync_WithMultipleJobsForSameUser_CreatesAllSuccessfully()
  {
    // Arrange
    var user = CreateTestUser();
    await SeedAsync(user);

    var job1 = CreateTestCleanPlaylistJob(user.Id, "playlist_1", "Clean Playlist 1");
    var job2 = CreateTestCleanPlaylistJob(user.Id, "playlist_2", "Clean Playlist 2");

    // Act
    var result1 = await _jobRepository.CreateAsync(job1);
    var result2 = await _jobRepository.CreateAsync(job2);

    // Assert
    Assert.NotNull(result1);
    Assert.NotNull(result2);
    Assert.NotEqual(result1.Id, result2.Id);
    Assert.Equal(user.Id, result1.UserId);
    Assert.Equal(user.Id, result2.UserId);
    Assert.Equal("playlist_1", result1.SourcePlaylistId);
    Assert.Equal("playlist_2", result2.SourcePlaylistId);
  }
}
