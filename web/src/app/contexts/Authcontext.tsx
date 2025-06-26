'use client';

import {
  createContext,
  useState,
  useEffect,
  useCallback,
  useMemo,
} from 'react';
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query';
import {
  getMe,
  User,
  MusicService,
  SignUpRequest,
  SignInRequest,
  signUp as apiSignUp,
  signIn as apiSignIn,
  signOut as apiSignOut,
  getConnectedServices,
  connectSpotify,
  connectAppleMusic,
  disconnectService as apiDisconnectService,
} from '../services/api';

interface AuthContextType {
  user: User | null;
  connectedServices: MusicService[];
  isAuthenticated: boolean;
  isLoading: boolean;
  requiresMusicServiceSetup: boolean;
  signUp: (data: SignUpRequest) => Promise<void>;
  signIn: (data: SignInRequest) => Promise<void>;
  signOut: () => Promise<void>;
  connectSpotify: () => void;
  connectAppleMusic: () => void;
  disconnectService: (service: string) => Promise<void>;
  // Legacy methods for backward compatibility
  login: () => void;
  logout: () => void;
}

export const AuthContext = createContext<AuthContextType | undefined>(
  undefined
);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const queryClient = useQueryClient();
  const [isInitialCheckComplete, setInitialCheckComplete] = useState(false);
  const [requiresMusicServiceSetup, setRequiresMusicServiceSetup] = useState(false);

  const {
    data: user,
    isLoading: isUserLoading,
    isError,
  } = useQuery({
    queryKey: ['me'],
    queryFn: getMe,
    retry: false,
    refetchOnWindowFocus: false,
    enabled: isInitialCheckComplete,
  });

  const {
    data: connectedServices = [],
    refetch: refetchServices,
  } = useQuery({
    queryKey: ['connectedServices'],
    queryFn: getConnectedServices,
    retry: false,
    refetchOnWindowFocus: false,
    enabled: !!user,
  });

  const signUpMutation = useMutation({
    mutationFn: apiSignUp,
    onSuccess: (data) => {
      queryClient.setQueryData(['me'], data.user);
      setRequiresMusicServiceSetup(data.requiresMusicServiceSetup || false);
      refetchServices();
    },
  });

  const signInMutation = useMutation({
    mutationFn: apiSignIn,
    onSuccess: (data) => {
      queryClient.setQueryData(['me'], data.user);
      setRequiresMusicServiceSetup(data.requiresMusicServiceSetup || false);
      refetchServices();
    },
  });

  const signOutMutation = useMutation({
    mutationFn: apiSignOut,
    onSuccess: () => {
      queryClient.setQueryData(['me'], null);
      queryClient.setQueryData(['connectedServices'], []);
      window.location.href = '/auth';
    },
  });

  const disconnectServiceMutation = useMutation({
    mutationFn: apiDisconnectService,
    onSuccess: () => {
      refetchServices();
    },
  });

  useEffect(() => {
    setInitialCheckComplete(true);
  }, []);

  const signUp = useCallback(async (data: SignUpRequest) => {
    try {
      await signUpMutation.mutateAsync(data);
    } catch (error) {
      throw error;
    }
  }, [signUpMutation]);

  const signIn = useCallback(async (data: SignInRequest) => {
    try {
      await signInMutation.mutateAsync(data);
    } catch (error) {
      throw error;
    }
  }, [signInMutation]);

  const signOut = useCallback(async () => {
    try {
      await signOutMutation.mutateAsync();
    } catch (error) {
      // Even if the API call fails, clear local state
      queryClient.setQueryData(['me'], null);
      queryClient.setQueryData(['connectedServices'], []);
      window.location.href = '/auth';
    }
  }, [signOutMutation, queryClient]);

  const disconnectService = useCallback(async (service: string) => {
    try {
      await disconnectServiceMutation.mutateAsync(service);
    } catch (error) {
      throw error;
    }
  }, [disconnectServiceMutation]);

  // Legacy methods for backward compatibility
  const login = useCallback(() => {
    // For legacy Spotify OAuth flow - redirect to auth page instead
    window.location.href = '/auth';
  }, []);

  const logout = useCallback(() => {
    signOut();
  }, [signOut]);

  const authContextValue = useMemo(
    () => ({
      user: user ?? null,
      connectedServices,
      isAuthenticated: !!user && !isError,
      isLoading: !isInitialCheckComplete || isUserLoading,
      requiresMusicServiceSetup,
      signUp,
      signIn,
      signOut,
      connectSpotify,
      connectAppleMusic,
      disconnectService,
      // Legacy methods
      login,
      logout,
    }),
    [
      user,
      connectedServices,
      isUserLoading,
      isInitialCheckComplete,
      isError,
      requiresMusicServiceSetup,
      signUp,
      signIn,
      signOut,
      disconnectService,
      login,
      logout,
    ]
  );

  return (
    <AuthContext.Provider value={authContextValue}>
      {children}
    </AuthContext.Provider>
  );
}