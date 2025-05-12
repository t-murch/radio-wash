# Spotify Clean Playlist Generator (RadioWash)

Create clean versions of your Spotify playlists by finding non-explicit alternatives to explicit tracks.

## Project Overview

RadioWash is a web application that allows Spotify users to create "clean" versions of their playlists by automatically replacing explicit tracks with their clean alternatives when available. The application uses the Spotify API to:

1. Authenticate with your Spotify account
2. List your existing playlists
3. Analyze playlists for explicit content
4. Find clean alternatives for explicit tracks
5. Create a new "clean" playlist with the alternatives

## Technical Stack

This project is built as an NX monorepo with:

- **Backend**: ASP.NET Core 8 API with Entity Framework Core
- **Frontend**: Next.js 15 with React, TypeScript, and Tailwind CSS
- **Database**: SQL Server (configurable)

## Prerequisites

To run this project, you'll need:

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. [Node.js](https://nodejs.org/) (v18+)
3. [pnpm](https://pnpm.io/installation) (v8+)
4. ~~[SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (or SQL Server Express)~~
5. A Spotify Developer account and registered application

## Setup Instructions

### 1. Clone the Repository

```bash
git clone https://github.com/yourusername/radiowash.git
cd radiowash
```

### 2. Install Dependencies

```bash
pnpm install
```

### 3. Register a Spotify Developer Application

1. Go to the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard/)
2. Create a new application
3. Set the redirect URI to `http://localhost:3000/auth/callback`
4. Note your Client ID and Client Secret

### 4. Configure the Backend

1. Update the Spotify credentials in `api/appsettings.json`:

```json
"Spotify": {
  "ClientId": "YOUR_SPOTIFY_CLIENT_ID",
  "ClientSecret": "YOUR_SPOTIFY_CLIENT_SECRET",
  "RedirectUri": "http://localhost:3000/auth/callback",
  "Scopes": [
    "user-read-private",
    "user-read-email",
    "playlist-read-private",
    "playlist-read-collaborative",
    "playlist-modify-public",
    "playlist-modify-private"
  ]
}
```

2. Update the JWT secret in `api/appsettings.json`:

```json
"Jwt": {
  "Secret": "YOUR_SUPER_SECRET_JWT_KEY_THAT_SHOULD_BE_AT_LEAST_32_CHARACTERS_LONG",
  "Issuer": "RadioWash",
  "Audience": "RadioWashFrontend",
  "ExpirationInMinutes": 1440
}
```

3. Set up your database connection string in `api/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=RadioWash;Trusted_Connection=True;MultipleActiveResultSets=true"
}
```

### 5. Run Database Migrations

```bash
cd api
dotnet ef database update
cd ..
```

### 6. Start the Application

Start the backend API:

```bash
npx nx serve api --configuration=https
```

In a new terminal, start the frontend:

```bash
npx nx serve web:dev --experimental-https
```

## Usage

1. Open your browser and navigate to `http://127.0.0.1:3000`
2. Click on "Connect with Spotify"
3. Authorize the application to access your Spotify account
4. Select a playlist you want to clean
5. Click "Create Clean Playlist"
6. Wait for the job to complete
7. Enjoy your new clean playlist on Spotify!

## Features

- **Authentication**: Secure Spotify OAuth 2.0 authentication
- **Playlist Management**: View all your Spotify playlists
- **Clean Playlist Creation**: Automatically find clean alternatives for explicit tracks
- **Job Tracking**: Monitor the progress of your clean playlist creation jobs
- **Track Mapping**: View detailed mappings between explicit tracks and their clean alternatives

## Architecture

The application follows a clean architecture approach:

- **Domain Models**: Represent the core business entities
- **Data Access**: Entity Framework Core for database operations
- **Services**: Business logic for authentication, Spotify API integration, and playlist processing
- **API Controllers**: RESTful endpoints for frontend communication
- **React Components**: Modular UI components for the frontend
- **API Service**: Frontend service for communication with the backend

## Limitations

- Not all explicit tracks have clean alternatives available on Spotify
- The track matching algorithm uses string similarity and may not always find the correct match
- The application requires Spotify API access, which may have rate limits

## Future Enhancements

- Improved track matching algorithm
- Batch processing of multiple playlists
- Custom filtering options (profanity, themes, etc.)
- User preferences for replacement strategies
- Support for Apple Music and other streaming platforms

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgements

- [Spotify Web API](https://developer.spotify.com/documentation/web-api/)
- [NX](https://nx.dev/)
- [ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/)
- [Next.js](https://nextjs.org/)
- [Tailwind CSS](https://tailwindcss.com/)
