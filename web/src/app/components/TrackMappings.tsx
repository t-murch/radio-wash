import { useState, useEffect } from 'react';
import { TrackMapping, getJobTrackMappings } from '@/services/api';

interface TrackMappingsProps {
  userId: number;
  jobId: number;
}

export default function TrackMappings({ userId, jobId }: TrackMappingsProps) {
  const [trackMappings, setTrackMappings] = useState<TrackMapping[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<
    'all' | 'explicit' | 'clean' | 'unmatched'
  >('all');

  useEffect(() => {
    const loadTrackMappings = async () => {
      try {
        setLoading(true);
        setError(null);

        const mappings = await getJobTrackMappings(userId, jobId);
        setTrackMappings(mappings);
      } catch (error) {
        console.error('Error loading track mappings:', error);
        setError('Failed to load track mappings');
      } finally {
        setLoading(false);
      }
    };

    loadTrackMappings();
  }, [userId, jobId]);

  const getFilteredMappings = () => {
    switch (filter) {
      case 'explicit':
        return trackMappings.filter((m) => m.isExplicit);
      case 'clean':
        return trackMappings.filter((m) => !m.isExplicit);
      case 'unmatched':
        return trackMappings.filter((m) => m.isExplicit && !m.hasCleanMatch);
      default:
        return trackMappings;
    }
  };

  const filteredMappings = getFilteredMappings();

  if (loading) {
    return <div className="text-center p-4">Loading track mappings...</div>;
  }

  if (error) {
    return (
      <div className="p-4 text-red-600 bg-red-100 rounded-lg">{error}</div>
    );
  }

  return (
    <div className="bg-white shadow rounded-lg p-6">
      <h2 className="text-xl font-semibold text-gray-900 mb-4">
        Track Mappings
      </h2>

      <div className="mb-4">
        <div className="flex flex-wrap gap-2">
          {(['all', 'explicit', 'clean', 'unmatched'] as const).map((f) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-3 py-1 text-sm rounded-full ${
                filter === f
                  ? 'bg-blue-600 text-white'
                  : 'bg-gray-200 text-gray-700 hover:bg-gray-300'
              }`}
            >
              {f.charAt(0).toUpperCase() + f.slice(1)}
              {f === 'all' ? ` (${trackMappings.length})` : null}
              {f === 'explicit'
                ? ` (${trackMappings.filter((m) => m.isExplicit).length})`
                : null}
              {f === 'clean'
                ? ` (${trackMappings.filter((m) => !m.isExplicit).length})`
                : null}
              {f === 'unmatched'
                ? ` (${
                    trackMappings.filter(
                      (m) => m.isExplicit && !m.hasCleanMatch
                    ).length
                  })`
                : null}
            </button>
          ))}
        </div>
      </div>

      {filteredMappings.length === 0 ? (
        <p className="text-gray-500">No tracks match the selected filter.</p>
      ) : (
        <div className="space-y-4">
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th
                    scope="col"
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                  >
                    Source Track
                  </th>
                  <th
                    scope="col"
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                  >
                    Artist
                  </th>
                  <th
                    scope="col"
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                  >
                    Explicit
                  </th>
                  <th
                    scope="col"
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                  >
                    Clean Version
                  </th>
                  <th
                    scope="col"
                    className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                  >
                    Clean Artist
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {filteredMappings.map((mapping) => (
                  <tr key={mapping.id}>
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                      <a
                        href={`https://open.spotify.com/track/${mapping.sourceTrackId}`}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="hover:underline"
                      >
                        {mapping.sourceTrackName}
                      </a>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {mapping.sourceArtistName}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {mapping.isExplicit ? (
                        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-800">
                          Explicit
                        </span>
                      ) : (
                        <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                          Clean
                        </span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                      {mapping.isExplicit ? (
                        mapping.hasCleanMatch ? (
                          <a
                            href={`https://open.spotify.com/track/${mapping.targetTrackId}`}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="hover:underline"
                          >
                            {mapping.targetTrackName}
                          </a>
                        ) : (
                          <span className="text-gray-400">
                            No clean version found
                          </span>
                        )
                      ) : (
                        <span className="text-gray-400">Already clean</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {mapping.isExplicit && mapping.hasCleanMatch
                        ? mapping.targetArtistName
                        : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
