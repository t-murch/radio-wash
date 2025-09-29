using RadioWash.Api.Infrastructure.Repositories;

namespace RadioWash.Api.Infrastructure.Patterns;

/// <summary>
/// Unit of Work pattern for transaction management
/// </summary>
public interface IUnitOfWork : IDisposable
{
    ICleanPlaylistJobRepository Jobs { get; }
    ITrackMappingRepository TrackMappings { get; }
    IUserRepository Users { get; }
    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}