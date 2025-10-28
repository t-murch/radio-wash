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
          background: 'linear-gradient(135deg, #16a34a 0%, #059669 100%)',
          borderRadius: '20%',
        }}
      >
        <div
          style={{
            fontSize: 20,
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
