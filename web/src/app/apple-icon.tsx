import { ImageResponse } from 'next/og';

export const runtime = 'edge';

export const size = {
  width: 180,
  height: 180,
};
export const contentType = 'image/png';

export default function AppleIcon() {
  return new ImageResponse(
    (
      <div
        style={{
          width: '100%',
          height: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          // Literal hex: Edge runtime, no CSS custom properties. Tracks --primary
          // and --primary-foreground (light) — update alongside icon.tsx.
          background: '#0f5f5c',
          borderRadius: '22.5%',
        }}
      >
        <div
          style={{
            fontSize: 96,
            fontWeight: 600,
            color: '#fbf8f2',
            fontFamily: 'Georgia, serif',
          }}
        >
          R
        </div>
      </div>
    ),
    {
      ...size,
    }
  );
}
