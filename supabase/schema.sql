-- RadioWash Supabase Schema for Auth Refactor
-- Run this in the Supabase SQL Editor

-- Create the table to store public user data and encrypted tokens
CREATE TABLE public.Users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    supabase_user_id UUID REFERENCES auth.users(id) ON DELETE CASCADE NOT NULL UNIQUE,
    spotify_id TEXT NOT NULL UNIQUE,
    email TEXT,
    display_name TEXT,
    encrypted_spotify_access_token TEXT NOT NULL,
    encrypted_spotify_refresh_token TEXT NOT NULL,
    spotify_token_expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMPTZ WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Create a trigger to automatically update the 'updated_at' column
CREATE OR REPLACE FUNCTION public.handle_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = timezone('utc'::text, now());
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER on_user_updated
BEFORE UPDATE ON public.Users
FOR EACH ROW
EXECUTE PROCEDURE public.handle_updated_at();

-- Enable Row Level Security (RLS)
ALTER TABLE public.Users ENABLE ROW LEVEL SECURITY;

-- Create a policy that allows users to read their own profile
-- The backend, using the service_role key, will bypass RLS for creating/updating users
CREATE POLICY "Users can view their own profile."
ON public.Users FOR SELECT
USING (auth.uid() = supabase_user_id);

-- Prevent users from updating their own profiles directly via the API
-- All updates should go through the secure .NET backend
CREATE POLICY "Users cannot update their own profiles."
ON public.Users FOR UPDATE
USING (false);