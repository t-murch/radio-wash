//@ts-check

const { composePlugins, withNx } = require('@nx/next');
const { withSentryConfig } = require('@sentry/nextjs');
const { url } = require('inspector');
const path = require('path');

/**
 * @type {import('@nx/next/plugins/with-nx').WithNxOptions}
 **/
const nextConfig = {
  nx: {
    // Set this to true if you would like to use SVGR
    // See: https://github.com/gregberge/svgr
    svgr: false,
  },
  // For Container Apps deployment with full SSR support
  output: 'standalone',
  // Include monorepo root for proper file tracing
  outputFileTracingRoot: path.join(__dirname, '../../'),
  // Optional: Enable if you need image optimization
  images: {
    dangerouslyAllowSVG: true,
    contentDispositionType: 'attachment',
    contentSecurityPolicy: "default-src 'self'; script-src 'none'; sandbox;",
    remotePatterns: [
      {
        protocol: 'https',
        hostname: 'i.scdn.co',
        port: '',
        pathname: '/**',
      },
      {
        protocol: 'https',
        hostname: 'mosaic.scdn.co',
        port: '',
        pathname: '/**',
      },
      {
        protocol: 'https',
        hostname: 'image-cdn-ak.spotifycdn.com',
        port: '',
        pathname: '/**',
      },
      {
        protocol: 'https',
        hostname: 'image-cdn-fa.spotifycdn.com',
        port: '',
        pathname: '/**',
      },
    ], // Spotify image domains
  },
};

const plugins = [
  // Add more Next.js plugins to this list if needed.
  withNx,
];

module.exports = composePlugins(...plugins)(
  withSentryConfig(nextConfig, {
    org: 'radiowash',
    project: 'javascript-nextjs',
    // Only print logs for uploading source maps in CI
    // Set to `true` to suppress logs
    silent: !process.env.CI,
    // Automatically tree-shake Sentry logger statements to reduce bundle size
    disableLogger: true,
  })
);
