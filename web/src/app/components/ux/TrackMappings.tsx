import { useState, useEffect } from 'react';
import { TrackMapping, getJobTrackMappings, Job } from '@/services/api';
import { trackUrl } from '@/lib/providers';

interface TrackMappingsProps {
  userId: number;
  jobId: number;
  job?: Job;
}

// Chip explaining HOW a copy-job track matched (ISRC bridge vs. search fallback).
function MatchMethodChip({ method }: { method?: string }) {
  if (!method || method === 'none') return null;
  const labels: Record<string, string> = {
    isrc: 'ISRC match',
    'isrc-clean': 'ISRC → clean',
    search: 'Search match',
    'search-clean': 'Search → clean',
  };
  const label = labels[method];
  if (!label) return null;
  return (
    <span className="inline-block text-[10px] uppercase tracking-wide bg-muted text-muted-foreground px-1.5 py-0.5 rounded">
      {label}
    </span>
  );
}

function TrackLink({
  provider,
  trackId,
  name,
  className,
}: {
  provider?: string;
  trackId: string;
  name: string;
  className: string;
}) {
  const url = trackUrl(provider as 'spotify' | 'apple_music' | undefined, trackId);
  if (!url) {
    return (
      <span className={className.replace('hover:underline', '')} title={name}>
        {name}
      </span>
    );
  }
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      className={className}
      title={name}
    >
      {name}
    </a>
  );
}

export default function TrackMappings({ userId, jobId, job }: TrackMappingsProps) {
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
        return trackMappings.filter((m) => m.hasCleanMatch);
      case 'unmatched':
        return trackMappings.filter((m) => m.isExplicit && !m.hasCleanMatch);
      default:
        return trackMappings;
    }
  };

  const filteredMappings = getFilteredMappings();

  const sourceProvider = job?.provider ?? 'spotify';
  const targetProvider = job?.targetProvider ?? 'spotify';
  const isCopy = job?.jobType === 'copy';
  const matchedColumnLabel = isCopy ? 'Matched Version' : 'Clean Version';

  const truncateText = (text: string, maxLength = 30) => {
    return text.length > maxLength
      ? text.substring(0, maxLength) + '...'
      : text;
  };

  const ExplicitBadge = ({
    isExplicit,
    hasCleanMatch,
  }: {
    isExplicit: boolean;
    hasCleanMatch: boolean;
  }) => {
    if (!isExplicit) {
      return <span className="text-success text-sm">✓</span>;
    }
    return hasCleanMatch ? (
      <span className="text-success text-sm">✓</span>
    ) : (
      <span className="text-warning text-sm">⚠</span>
    );
  };

  if (loading) {
    return <div className="text-center p-4">Loading track mappings...</div>;
  }

  if (error) {
    return (
      <div className="p-4 text-error bg-error-muted rounded-lg">{error}</div>
    );
  }

  return (
    <div className="bg-card shadow rounded-lg">
      {/* Fixed Header */}
      <div className="p-6 border-b border bg-card rounded-t-lg">
        <h2 className="text-xl font-semibold text-foreground mb-4">
          Track Mappings
        </h2>

        {/* Filter buttons */}
        <div className="flex flex-wrap gap-2">
          {(['all', 'explicit', 'clean', 'unmatched'] as const).map((f) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-3 py-1 text-sm rounded-full ${
                filter === f
                  ? 'bg-info text-info-foreground'
                  : 'bg-muted text-muted-foreground hover:bg-accent'
              }`}
            >
              {f.charAt(0).toUpperCase() + f.slice(1)}
              {f === 'all' ? ` (${trackMappings.length})` : null}
              {f === 'explicit'
                ? ` (${trackMappings.filter((m) => m.isExplicit).length})`
                : null}
              {f === 'clean'
                ? ` (${trackMappings.filter((m) => m.hasCleanMatch).length})`
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

      {/* Scrollable Content */}
      <div className="max-h-[500px] overflow-y-auto">
        {filteredMappings.length === 0 ? (
          <div className="p-6">
            <p className="text-muted-foreground">
              No tracks match the selected filter.
            </p>
          </div>
        ) : (
          <>
            {/* Desktop Table View */}
            <div className="hidden lg:block">
              <table className="min-w-full divide-y divide-border">
                <thead className="bg-muted/50 sticky top-0 z-10">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase tracking-wider w-1/3">
                      Original Track
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase tracking-wider w-12">
                      Status
                    </th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-muted-foreground uppercase tracking-wider w-1/3">
                      {matchedColumnLabel}
                    </th>
                  </tr>
                </thead>
                <tbody className="bg-card divide-y divide-border">
                  {filteredMappings.map((mapping) => (
                    <tr key={mapping.id} className="hover:bg-muted/50">
                      <td className="px-4 py-4">
                        <div className="space-y-1">
                          <TrackLink
                            provider={sourceProvider}
                            trackId={mapping.sourceTrackId}
                            name={truncateText(mapping.sourceTrackName, 40)}
                            className="block text-sm font-medium text-foreground hover:underline"
                          />
                          <p
                            className="text-sm text-muted-foreground"
                            title={mapping.sourceArtistName}
                          >
                            {truncateText(mapping.sourceArtistName, 40)}
                          </p>
                        </div>
                      </td>
                      <td className="px-4 py-4 text-start">
                        <ExplicitBadge
                          isExplicit={mapping.isExplicit}
                          hasCleanMatch={mapping.hasCleanMatch}
                        />
                      </td>
                      <td className="px-4 py-4">
                        {mapping.hasCleanMatch && mapping.targetTrackId ? (
                          <div className="space-y-1">
                            <TrackLink
                              provider={targetProvider}
                              trackId={mapping.targetTrackId}
                              name={truncateText(mapping.targetTrackName || '', 40)}
                              className="block text-sm font-medium text-foreground hover:underline"
                            />
                            <p
                              className="text-sm text-muted-foreground"
                              title={mapping.targetArtistName || ''}
                            >
                              {truncateText(mapping.targetArtistName || '', 40)}
                            </p>
                            {isCopy && <MatchMethodChip method={mapping.matchMethod} />}
                          </div>
                        ) : mapping.isExplicit || isCopy ? (
                          <span className="text-sm text-muted-foreground">
                            {isCopy ? 'No match found' : 'No clean version found'}
                          </span>
                        ) : (
                          <span className="text-sm text-muted-foreground">
                            Already clean
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Mobile/Tablet Card View */}
            <div className="lg:hidden p-4 space-y-4">
              {filteredMappings.map((mapping) => (
                <div
                  key={mapping.id}
                  className="border rounded-lg p-4 bg-muted/50"
                >
                  <div className="flex items-start justify-between mb-3">
                    <div className="flex-1 min-w-0">
                      <TrackLink
                        provider={sourceProvider}
                        trackId={mapping.sourceTrackId}
                        name={mapping.sourceTrackName}
                        className="block text-sm font-medium text-foreground hover:underline truncate"
                      />
                      <p
                        className="text-sm text-muted-foreground truncate"
                        title={mapping.sourceArtistName}
                      >
                        {mapping.sourceArtistName}
                      </p>
                    </div>
                    <div className="ml-2 flex-shrink-0">
                      <ExplicitBadge
                        isExplicit={mapping.isExplicit}
                        hasCleanMatch={mapping.hasCleanMatch}
                      />
                    </div>
                  </div>

                  {(mapping.isExplicit || isCopy) && (
                    <div className="pt-3 border-t border">
                      <p className="text-xs font-medium text-muted-foreground uppercase tracking-wider mb-2">
                        {matchedColumnLabel}
                      </p>
                      {mapping.hasCleanMatch && mapping.targetTrackId ? (
                        <div>
                          <TrackLink
                            provider={targetProvider}
                            trackId={mapping.targetTrackId}
                            name={mapping.targetTrackName || ''}
                            className="block text-sm font-medium text-foreground hover:underline truncate"
                          />
                          <p
                            className="text-sm text-muted-foreground truncate"
                            title={mapping.targetArtistName || ''}
                          >
                            {mapping.targetArtistName}
                          </p>
                          {isCopy && <MatchMethodChip method={mapping.matchMethod} />}
                        </div>
                      ) : (
                        <span className="text-sm text-muted-foreground">
                          {isCopy ? 'No match found' : 'No clean version found'}
                        </span>
                      )}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
