'use client';

import { useState, useEffect, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useAuth } from '../hooks/useAuth';
import { Check, Music, AlertCircle } from 'lucide-react';

interface OnboardingStep {
  id: string;
  title: string;
  description: string;
  completed: boolean;
}

function OnboardingPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { user, connectedServices, connectSpotify, connectAppleMusic, isAuthenticated } = useAuth();
  const [currentStep, setCurrentStep] = useState(0);
  const [error, setError] = useState<string | null>(null);

  // Check for error messages from URL params
  useEffect(() => {
    const errorParam = searchParams.get('error');
    if (errorParam) {
      const errorMessages: Record<string, string> = {
        'spotify_auth_failed': 'Failed to connect to Spotify. Please try again.',
        'spotify_connection_failed': 'Failed to connect to Spotify. Please try again.',
        'apple_auth_failed': 'Failed to connect to Apple Music. Please try again.',
        'apple_connection_failed': 'Failed to connect to Apple Music. Please try again.',
        'invalid_state': 'Authentication failed. Please try connecting again.',
      };
      setError(errorMessages[errorParam] || 'An error occurred. Please try again.');
      
      // Clear error after 5 seconds
      const timer = setTimeout(() => setError(null), 5000);
      return () => clearTimeout(timer);
    }
  }, [searchParams]);

  // Redirect to auth if not authenticated
  useEffect(() => {
    if (!isAuthenticated) {
      router.push('/auth');
      return;
    }
  }, [isAuthenticated, router]);

  // Check if user has any connected services
  const hasConnectedServices = connectedServices.length > 0;

  // Define onboarding steps
  const steps: OnboardingStep[] = [
    {
      id: 'welcome',
      title: 'Welcome to RadioWash!',
      description: 'Let\'s get your account set up so you can start cleaning your playlists.',
      completed: !!user,
    },
    {
      id: 'connect-music',
      title: 'Connect Your Music Service',
      description: 'You need to connect at least one music service to use RadioWash.',
      completed: hasConnectedServices,
    },
    {
      id: 'ready',
      title: 'You\'re All Set!',
      description: 'Your account is ready. Let\'s start cleaning some playlists!',
      completed: hasConnectedServices,
    },
  ];

  // Auto-advance steps
  useEffect(() => {
    if (currentStep < steps.length - 1 && steps[currentStep].completed) {
      const timer = setTimeout(() => {
        setCurrentStep(prev => prev + 1);
      }, 1000);
      return () => clearTimeout(timer);
    }
  }, [currentStep, steps]);

  // Redirect to dashboard when onboarding is complete
  useEffect(() => {
    if (hasConnectedServices && currentStep === steps.length - 1) {
      const timer = setTimeout(() => {
        router.push('/dashboard');
      }, 2000);
      return () => clearTimeout(timer);
    }
  }, [hasConnectedServices, currentStep, steps.length, router]);

  const handleSpotifyConnect = () => {
    connectSpotify();
  };

  const handleAppleMusicConnect = () => {
    // connectAppleMusic(); // Commented out as it's not ready yet
    alert('Apple Music integration coming soon!');
  };

  const handleSkipToComplete = () => {
    if (hasConnectedServices) {
      router.push('/dashboard');
    }
  };

  if (!isAuthenticated || !user) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-green-500"></div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-green-50 to-blue-50 dark:from-gray-900 dark:to-gray-800">
      <div className="container mx-auto px-4 py-8">
        {/* Progress Header */}
        <div className="text-center mb-8">
          <h1 className="text-3xl font-bold text-gray-900 dark:text-white mb-2">
            Account Setup
          </h1>
          <div className="flex items-center justify-center space-x-4 mb-6">
            {steps.map((step, index) => (
              <div key={step.id} className="flex items-center">
                <div
                  className={`w-8 h-8 rounded-full flex items-center justify-center ${
                    step.completed
                      ? 'bg-green-500 text-white'
                      : index === currentStep
                      ? 'bg-blue-500 text-white'
                      : 'bg-gray-300 text-gray-600'
                  }`}
                >
                  {step.completed ? (
                    <Check className="w-5 h-5" />
                  ) : (
                    <span className="text-sm font-semibold">{index + 1}</span>
                  )}
                </div>
                {index < steps.length - 1 && (
                  <div
                    className={`w-16 h-1 mx-2 ${
                      steps[index + 1].completed ? 'bg-green-500' : 'bg-gray-300'
                    }`}
                  />
                )}
              </div>
            ))}
          </div>
        </div>

        {/* Error Message */}
        {error && (
          <div className="max-w-2xl mx-auto mb-6">
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4">
              <div className="flex items-center">
                <AlertCircle className="w-5 h-5 text-red-500 dark:text-red-400 mr-3" />
                <p className="text-red-700 dark:text-red-300">{error}</p>
              </div>
            </div>
          </div>
        )}

        {/* Current Step Content */}
        <div className="max-w-2xl mx-auto">
          <div className="bg-white dark:bg-gray-800 rounded-lg shadow-lg p-8">
            <div className="text-center mb-6">
              <h2 className="text-2xl font-semibold text-gray-900 dark:text-white mb-2">
                {steps[currentStep].title}
              </h2>
              <p className="text-gray-600 dark:text-gray-300">
                {steps[currentStep].description}
              </p>
            </div>

            {/* Step-specific content */}
            {currentStep === 0 && (
              <div className="text-center">
                <div className="mb-6">
                  <div className="w-16 h-16 bg-green-100 dark:bg-green-900 rounded-full flex items-center justify-center mx-auto mb-4">
                    <Music className="w-8 h-8 text-green-600 dark:text-green-400" />
                  </div>
                  <p className="text-lg text-gray-700 dark:text-gray-300">
                    Hi {user.displayName}! Welcome to RadioWash.
                  </p>
                </div>
              </div>
            )}

            {currentStep === 1 && (
              <div className="space-y-4">
                <p className="text-center text-gray-600 dark:text-gray-400 mb-6">
                  Choose a music service to connect. You need at least one to start cleaning playlists.
                </p>
                
                {/* Music Service Options */}
                <div className="space-y-4">
                  {/* Spotify */}
                  <div className={`border-2 rounded-lg p-4 transition-all cursor-pointer hover:shadow-md ${
                    connectedServices.some(s => s.serviceType === 'Spotify')
                      ? 'border-green-500 bg-green-50 dark:bg-green-900/20'
                      : 'border-gray-200 dark:border-gray-600 hover:border-green-300'
                  }`}>
                    <div className="flex items-center justify-between">
                      <div className="flex items-center space-x-3">
                        <div className="w-10 h-10 bg-green-500 rounded-lg flex items-center justify-center">
                          <Music className="w-6 h-6 text-white" />
                        </div>
                        <div>
                          <h3 className="font-semibold text-gray-900 dark:text-white">Spotify</h3>
                          <p className="text-sm text-gray-600 dark:text-gray-400">
                            Connect your Spotify account to manage playlists
                          </p>
                        </div>
                      </div>
                      {connectedServices.some(s => s.serviceType === 'Spotify') ? (
                        <div className="flex items-center text-green-600 dark:text-green-400">
                          <Check className="w-5 h-5 mr-1" />
                          <span className="text-sm font-medium">Connected</span>
                        </div>
                      ) : (
                        <button
                          onClick={handleSpotifyConnect}
                          className="px-4 py-2 bg-green-500 text-white rounded-lg hover:bg-green-600 transition-colors"
                        >
                          Connect
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Apple Music */}
                  <div className="border-2 border-gray-200 dark:border-gray-600 rounded-lg p-4 opacity-60">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center space-x-3">
                        <div className="w-10 h-10 bg-gray-400 rounded-lg flex items-center justify-center">
                          <Music className="w-6 h-6 text-white" />
                        </div>
                        <div>
                          <h3 className="font-semibold text-gray-900 dark:text-white">Apple Music</h3>
                          <p className="text-sm text-gray-600 dark:text-gray-400">
                            Coming soon! Connect your Apple Music account
                          </p>
                        </div>
                      </div>
                      <button
                        onClick={handleAppleMusicConnect}
                        disabled
                        className="px-4 py-2 bg-gray-300 text-gray-500 rounded-lg cursor-not-allowed"
                      >
                        Coming Soon
                      </button>
                    </div>
                  </div>
                </div>

                {hasConnectedServices && (
                  <div className="text-center pt-6">
                    <button
                      onClick={handleSkipToComplete}
                      className="px-6 py-3 bg-blue-500 text-white rounded-lg hover:bg-blue-600 transition-colors"
                    >
                      Continue to Dashboard
                    </button>
                  </div>
                )}
              </div>
            )}

            {currentStep === 2 && (
              <div className="text-center">
                <div className="mb-6">
                  <div className="w-16 h-16 bg-green-100 dark:bg-green-900 rounded-full flex items-center justify-center mx-auto mb-4">
                    <Check className="w-8 h-8 text-green-600 dark:text-green-400" />
                  </div>
                  <p className="text-lg text-gray-700 dark:text-gray-300 mb-4">
                    Perfect! Your account is all set up.
                  </p>
                  <p className="text-gray-600 dark:text-gray-400">
                    You'll be redirected to your dashboard in a moment...
                  </p>
                </div>
                
                <button
                  onClick={() => router.push('/dashboard')}
                  className="px-6 py-3 bg-green-500 text-white rounded-lg hover:bg-green-600 transition-colors"
                >
                  Go to Dashboard Now
                </button>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default function OnboardingPage() {
  return (
    <Suspense fallback={
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-green-500"></div>
      </div>
    }>
      <OnboardingPageContent />
    </Suspense>
  );
}