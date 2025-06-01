'use client';
import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import LandingPage from './components/LandingPage';

export default function HomePage() {
  const router = useRouter();

  useEffect(() => {
    const token = localStorage.getItem('radiowash_token');
    if (token) {
      router.push('/dashboard');
    }
  }, [router]);

  return <LandingPage />;
}
