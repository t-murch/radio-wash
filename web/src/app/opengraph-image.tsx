import { ImageResponse } from 'next/og';

export const runtime = 'edge';

export const alt = 'RadioWash - Clean Your Spotify Playlists';
export const size = {
  width: 1200,
  height: 630,
};
export const contentType = 'image/png';

export default async function Image() {
  return new ImageResponse(
    (
      <div
        style={{
          height: '100%',
          width: '100%',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: '#fff',
          backgroundImage:
            'linear-gradient(135deg, #dcfce7 0%, #dbeafe 100%)',
        }}
      >
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '80px',
          }}
        >
          <div
            style={{
              fontSize: 72,
              fontWeight: 'bold',
              color: '#16a34a',
              marginBottom: 20,
            }}
          >
            RadioWash
          </div>
          <div
            style={{
              fontSize: 40,
              color: '#1f2937',
              textAlign: 'center',
              maxWidth: 900,
              lineHeight: 1.4,
            }}
          >
            Transform Explicit Spotify Playlists into Clean Versions
          </div>
          <div
            style={{
              fontSize: 28,
              color: '#6b7280',
              marginTop: 30,
              textAlign: 'center',
            }}
          >
            AI-Powered • 80%+ Success Rate • Completely Free
          </div>
        </div>
      </div>
    ),
    {
      ...size,
    }
  );
}
