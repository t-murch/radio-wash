import { Job } from '@/services/api';
import Link from 'next/link';

export function JobCard({ job }: { job: Job }) {
  const getStatusStyles = () => {
    switch (job.status) {
      case 'Completed':
        return 'bg-green-100 text-green-800 border-green-300';
      case 'Processing':
        return 'bg-blue-100 text-blue-800 border-blue-300';
      case 'Failed':
        return 'bg-red-100 text-red-800 border-red-300';
      default:
        return 'bg-gray-100 text-gray-800 border-gray-300';
    }
  };
  const progress =
    job.totalTracks > 0 ? (job.processedTracks / job.totalTracks) * 100 : 0;
  return (
    <Link
      href={`/jobs/${job.id}`}
      className="block bg-white border border-gray-200 rounded-lg p-4 shadow-sm hover:shadow-md transition-shadow"
    >
      <div className="flex justify-between items-start mb-2">
        <div className="w-4/5">
          <h3
            className="font-bold text-gray-800 truncate"
            title={job.targetPlaylistName}
          >
            {job.targetPlaylistName}
          </h3>
          <p
            className="text-sm text-gray-500 truncate"
            title={job.sourcePlaylistName}
          >
            From: {job.sourcePlaylistName}
          </p>
        </div>
        <span
          className={`text-xs font-semibold px-2 py-1 rounded-full border ${getStatusStyles()}`}
        >
          {job.status}
        </span>
      </div>
      {/* {job.status === 'Processing' && ( */}
      {/*   <div className="mt-2"> */}
      {/*     <div className="w-full bg-gray-200 rounded-full h-2"> */}
      {/*       <div */}
      {/*         className="bg-blue-500 h-2 rounded-full" */}
      {/*         style={{ width: `${progress}%` }} */}
      {/*       ></div> */}
      {/*     </div> */}
      {/*     <p className="text-xs text-right text-gray-500 mt-1"> */}
      {/*       {job.processedTracks} of {job.totalTracks} */}
      {/*     </p> */}
      {/*   </div> */}
      {/* )} */}
      <p className="text-xs text-gray-400 mt-2 text-right">
        Updated: {new Date(job.updatedAt).toLocaleString()}
      </p>
    </Link>
  );
}
