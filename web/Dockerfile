# Next.js Dockerfile for NX Monorepo and Azure Container Apps
FROM node:22-alpine AS base

# Install dependencies only when needed
FROM base AS deps
RUN apk add --no-cache libc6-compat
WORKDIR /app

# Copy package management files
COPY package.json pnpm-lock.yaml* ./
COPY .npmrc ./

# Install pnpm and dependencies
# IMPORTANT: Use --ignore-scripts to skip the prepare script that requires nx-dotnet
RUN corepack enable pnpm && pnpm i --frozen-lockfile --ignore-scripts

# Rebuild the source code only when needed
FROM base AS builder
WORKDIR /app

# Copy dependencies
COPY --from=deps /app/node_modules ./node_modules

# Copy necessary files for NX build
COPY . .

# Build args for environment variables
ARG NEXT_PUBLIC_API_URL
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL

# Remove dotnet plugin for web build
RUN apk update && apk add jq
RUN jq 'del(.plugins[] | select(. == "@nx-dotnet/core"))' nx.json > nx-web.json && mv nx-web.json nx.json

# Build the web application using NX
# The nx build command will work fine without the dotnet plugin for web projects
RUN corepack enable pnpm && pnpm nx build web --skip-nx-cache --configuration=production

# Production image, copy all the files and run next
FROM base AS runner
WORKDIR /app

ENV NODE_ENV=production

# Create nextjs user
RUN addgroup --system --gid 1001 nodejs
RUN adduser --system --uid 1001 nextjs

# Copy the standalone build from the NX dist directory
COPY --from=builder --chown=nextjs:nodejs /app/dist/web/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/dist/web/.next/static ./dist/web/.next/static
COPY --from=builder --chown=nextjs:nodejs /app/dist/web/public ./dist/web/public

# Set correct permissions
USER nextjs

EXPOSE 3000

ENV PORT=3000
ENV HOSTNAME="0.0.0.0"

# Start the application
CMD ["node", "dist/web/server.js"]
