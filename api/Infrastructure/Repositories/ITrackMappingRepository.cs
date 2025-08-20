using RadioWash.Api.Models.Domain;

namespace RadioWash.Api.Infrastructure.Repositories;

public interface ITrackMappingRepository
{
    Task<IEnumerable<TrackMapping>> GetByJobIdAsync(int jobId);
    Task<TrackMapping> CreateAsync(TrackMapping trackMapping);
    Task AddRangeAsync(IEnumerable<TrackMapping> trackMappings);
    Task<TrackMapping> UpdateAsync(TrackMapping trackMapping);
    Task DeleteAsync(TrackMapping trackMapping);
    Task SaveChangesAsync();
}