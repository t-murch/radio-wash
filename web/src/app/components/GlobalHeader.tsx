'use client';

import React from 'react';
import { User } from 'lucide-react';
import Image from 'next/image';
import Link from 'next/link';
import { useRouter } from 'next/navigation';

import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { ThemeToggle } from '@/components/ui/theme-toggle';
import { createClient } from '@/lib/supabase/client';
import type { User as ApiUser } from '@/services/api';
import { useSubscriptionStatus } from '@/hooks/useSubscriptionSync';
import { setSentryUser, clearSentryUser } from '@/lib/sentry-user-context';
import { AttachToFeedbackButton } from './ux/ReportBug-Btn';

interface GlobalHeaderProps {
  user?: ApiUser | null;
  showBackButton?: boolean;
  backButtonHref?: string;
  backButtonLabel?: string;
}

export function GlobalHeader({
  user,
  showBackButton = false,
  backButtonHref = '/dashboard',
  backButtonLabel = 'Back to Dashboard',
}: GlobalHeaderProps) {
  const router = useRouter();
  const supabase = createClient();
  const { data: subscriptionStatus } = useSubscriptionStatus();

  // Set Sentry user context when user changes
  React.useEffect(() => {
    setSentryUser(user || null);
  }, [user]);

  const handleSignOut = async () => {
    clearSentryUser(); // Clear Sentry context before sign out
    await supabase.auth.signOut();
    router.push('/');
  };

  const handleSignIn = () => {
    router.push('/auth');
  };

  return (
    <header className="bg-card border-b sticky top-0 z-50">
      <div className="max-w-7xl mx-auto py-3 px-4 sm:px-6 lg:px-8 flex justify-between items-center">
        <div className="flex items-center space-x-4">
          {showBackButton && (
            <Button variant="ghost" size="sm" asChild>
              <Link href={backButtonHref}>← {backButtonLabel}</Link>
            </Button>
          )}
          <Link href={user ? '/dashboard' : '/'} className="flex items-center">
            <h1 className="text-2xl font-bold text-green-600">RadioWash</h1>
          </Link>
        </div>

        <div className="flex items-center space-x-3">
          <ThemeToggle />

          {user ? (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  className="relative h-10 w-10 rounded-md"
                >
                  {user.profileImageUrl ? (
                    <Image
                      src={user.profileImageUrl}
                      alt="User Profile"
                      className="rounded-full"
                      width={32}
                      height={32}
                    />
                  ) : (
                    <User className="h-5 w-5" />
                  )}
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent className="w-56" align="end" forceMount>
                <DropdownMenuLabel className="font-normal">
                  <div className="flex flex-col space-y-1">
                    <p className="text-sm font-medium leading-none">
                      {user.displayName}
                    </p>
                    <p className="text-xs leading-none text-muted-foreground">
                      {user.email}
                    </p>
                  </div>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem asChild>
                  <Link href="/dashboard">Dashboard</Link>
                </DropdownMenuItem>
                <DropdownMenuItem asChild>
                  <Link href="/dashboard/sync">Sync Management</Link>
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem asChild>
                  <Link href="/subscription" className="flex items-center justify-between">
                    <span>
                      {subscriptionStatus?.hasActiveSubscription 
                        ? 'Manage Subscription' 
                        : 'Upgrade to Auto-Sync'}
                    </span>
                    {subscriptionStatus?.hasActiveSubscription ? (
                      <span className="ml-2 px-2 py-0.5 bg-success-muted text-success text-xs rounded-full">
                        Pro
                      </span>
                    ) : (
                      <span className="ml-2 px-2 py-0.5 bg-brand/20 text-brand text-xs rounded-full">
                        Free
                      </span>
                    )}
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem asChild>
                  <AttachToFeedbackButton />
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={handleSignOut}>
                  Sign out
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          ) : (
            <Button onClick={handleSignIn}>Sign In</Button>
          )}
        </div>
      </div>
    </header>
  );
}
