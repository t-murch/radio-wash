'use client';

import { useState } from 'react';
import { useAuth } from '../../hooks/useAuth';

export default function MusicServiceConnections() {
  const { connectedServices, connectSpotify, connectAppleMusic, disconnectService } = useAuth();
  const [disconnectingService, setDisconnectingService] = useState<string | null>(null);

  const handleDisconnect = async (serviceType: string) => {
    setDisconnectingService(serviceType);
    try {
      await disconnectService(serviceType.toLowerCase());
    } catch (error) {
      console.error('Failed to disconnect service:', error);
    } finally {
      setDisconnectingService(null);
    }
  };

  const isSpotifyConnected = connectedServices.some(
    (service) => service.serviceType === 'Spotify' && service.isActive
  );

  const isAppleMusicConnected = connectedServices.some(
    (service) => service.serviceType === 'AppleMusic' && service.isActive
  );

  return (
    <div className="bg-white shadow rounded-lg p-6">
      <h2 className="text-xl font-semibold text-gray-900 mb-6">Music Services</h2>
      
      <div className="space-y-4">
        {/* Spotify Connection */}
        <div className="flex items-center justify-between p-4 border border-gray-200 rounded-lg">
          <div className="flex items-center space-x-4">
            <div className="w-10 h-10 bg-green-500 rounded-full flex items-center justify-center">
              <span className="text-white font-bold text-sm">S</span>
            </div>
            <div>
              <h3 className="text-lg font-medium text-gray-900">Spotify</h3>
              <p className="text-sm text-gray-500">
                {isSpotifyConnected 
                  ? 'Connected - Access your Spotify playlists'
                  : 'Connect to access your Spotify playlists'
                }
              </p>
            </div>
          </div>
          
          <div className="flex items-center space-x-2">
            {isSpotifyConnected ? (
              <>
                <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                  Connected
                </span>
                <button
                  onClick={() => handleDisconnect('Spotify')}
                  disabled={disconnectingService === 'Spotify'}
                  className="text-sm text-red-600 hover:text-red-700 disabled:opacity-50"
                >
                  {disconnectingService === 'Spotify' ? 'Disconnecting...' : 'Disconnect'}
                </button>
              </>
            ) : (
              <button
                onClick={connectSpotify}
                className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md text-white bg-green-600 hover:bg-green-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-green-500"
              >
                Connect Spotify
              </button>
            )}
          </div>
        </div>

        {/* Apple Music Connection */}
        <div className="flex items-center justify-between p-4 border border-gray-200 rounded-lg">
          <div className="flex items-center space-x-4">
            <div className="w-10 h-10 bg-gray-900 rounded-full flex items-center justify-center">
              <span className="text-white font-bold text-sm">♪</span>
            </div>
            <div>
              <h3 className="text-lg font-medium text-gray-900">Apple Music</h3>
              <p className="text-sm text-gray-500">
                {isAppleMusicConnected 
                  ? 'Connected - Access your Apple Music playlists'
                  : 'Connect to access your Apple Music playlists'
                }
              </p>
            </div>
          </div>
          
          <div className="flex items-center space-x-2">
            {isAppleMusicConnected ? (
              <>
                <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                  Connected
                </span>
                <button
                  onClick={() => handleDisconnect('AppleMusic')}
                  disabled={disconnectingService === 'AppleMusic'}
                  className="text-sm text-red-600 hover:text-red-700 disabled:opacity-50"
                >
                  {disconnectingService === 'AppleMusic' ? 'Disconnecting...' : 'Disconnect'}
                </button>
              </>
            ) : (
              <button
                onClick={connectAppleMusic}
                disabled={true} // Disabled until Apple Music implementation is complete
                className="inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-400 bg-gray-100 cursor-not-allowed"
              >
                Coming Soon
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Info Section */}
      <div className="mt-6 p-4 bg-blue-50 rounded-lg">
        <h4 className="text-sm font-medium text-blue-900 mb-2">About Music Services</h4>
        <ul className="text-sm text-blue-800 space-y-1">
          <li>• Connect multiple music services to access all your playlists</li>
          <li>• You can create clean playlists from any connected service</li>
          <li>• Your connections are secure and can be removed at any time</li>
          <li>• RadioWash only accesses playlist data, not personal information</li>
        </ul>
      </div>

      {connectedServices.length === 0 && (
        <div className="mt-6 p-4 bg-yellow-50 rounded-lg">
          <p className="text-sm text-yellow-800">
            <strong>No music services connected.</strong> Connect at least one service to start creating clean playlists.
          </p>
        </div>
      )}
    </div>
  );
}