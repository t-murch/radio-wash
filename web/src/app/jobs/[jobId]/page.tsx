import * as Sentry from '@sentry/nextjs';
import { Metadata } from 'next';
import { redirect } from 'next/navigation';

import { createClient } from '@/lib/supabase/server';
import { getMeServer, getUserJobDetailsServer } from '@/services/api';
import { JobDetailsClient } from './job-details-client';

// Dynamic route: per-request Sentry trace metadata, see dashboard/page.tsx.
export function generateMetadata(): Metadata {
  return {
    title: 'Job details',
    robots: { index: false },
    other: { ...Sentry.getTraceData() },
  };
}

// The params object will contain the jobId from the URL
type JobPageProps = {
  params: Promise<{ jobId: string }>;
};

export default async function JobDetailsPage({ params }: JobPageProps) {
  const supabase = await createClient();
  const jI = (await params).jobId;
  const jobId = parseInt(jI, 10);

  const {
    data: { user },
  } = await supabase.auth.getUser();

  if (!user) {
    redirect('/auth');
  }

  // If the jobId from the URL is not a valid number, redirect.
  if (isNaN(jobId)) {
    redirect('/dashboard');
  }

  // Fetch initial data on the server in parallel.
  // The user ID is derived from the JWT on the backend, so we pass a placeholder.
  const [me, jobDetails] = await Promise.all([
    getMeServer(),
    getUserJobDetailsServer(jobId),
  ]);

  // If the job doesn't exist or doesn't belong to the user, the API will handle it.
  // We can redirect here if jobDetails is null.
  if (!jobDetails) {
    redirect('/dashboard?error=job_not_found');
  }

  return (
    <JobDetailsClient initialMe={me} initialJob={jobDetails} jobId={jobId} />
  );
}
