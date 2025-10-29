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
          background: 'linear-gradient(135deg, #16a34a 0%, #059669 100%)',
          borderRadius: '22.5%',
        }}
      >
        <div
          style={{
            fontSize: 96,
            fontWeight: 'bold',
            color: 'white',
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
