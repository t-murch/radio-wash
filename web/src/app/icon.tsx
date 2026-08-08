import { ImageResponse } from 'next/og';

export const runtime = 'edge';

export const size = {
  width: 32,
  height: 32,
};
export const contentType = 'image/png';

export default function Icon() {
  return new ImageResponse(
    (
      <div
        style={{
          width: '100%',
          height: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          // Literal hex: this renders in the Edge runtime, where the CSS custom
          // properties in globals.css are not available. Values track --primary
          // and --primary-foreground (light) — update both together.
          background: '#0f5f5c',
          borderRadius: '20%',
        }}
      >
        <div
          style={{
            fontSize: 20,
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
