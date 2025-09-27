using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RadioWash.Api.Infrastructure.Data;
using RadioWash.Api.Infrastructure.Repositories;

namespace RadioWash.Api.Infrastructure.Patterns;

/// <summary>
/// Entity Framework implementation of Unit of Work pattern
/// Manages transactions and coordinates multiple repositories
/// </summary>
public class EntityFrameworkUnitOfWork : IUnitOfWork
{
    private readonly RadioWashDbContext _context;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    public ICleanPlaylistJobRepository Jobs { get; }
    public ITrackMappingRepository TrackMappings { get; }
    public IUserRepository Users { get; }

    public EntityFrameworkUnitOfWork(
        RadioWashDbContext context,
        ICleanPlaylistJobRepository jobs,
        ITrackMappingRepository trackMappings,
        IUserRepository users)
    {
        _context = context;
        Jobs = jobs;
        TrackMappings = trackMappings;
        Users = users;
    }

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public async Task BeginTransactionAsync()
    {
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        if (_transaction == null)
            throw new InvalidOperationException("Transaction has not been started");

        await _transaction.CommitAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync()
    {
        if (_transaction == null)
            throw new InvalidOperationException("Transaction has not been started");

        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _transaction?.Dispose();
                _context.Dispose();
            }
            _disposed = true;
        }
    }
}