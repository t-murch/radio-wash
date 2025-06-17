'use client';

import { useState } from 'react';
import {
  Share2,
  Copy,
  Check,
  Twitter,
  Facebook,
  MessageCircle,
  Mail,
  ExternalLink,
  Sparkles,
} from 'lucide-react';
// import { Button } from '@/components/ui/button';
// import {
//   DropdownMenu,
//   DropdownMenuContent,
//   DropdownMenuItem,
//   DropdownMenuSeparator,
//   DropdownMenuTrigger,
// } from '@/components/ui/dropdown-menu';
// import { toast } from '@/hooks/use-toast';

interface ShareButtonProps {
  playlistName: string;
  playlistUrl: string;
  trackCount: number;
  successRate?: number;
  size?: 'sm' | 'default' | 'lg';
  variant?: 'default' | 'outline' | 'ghost';
  onShare?: (platform: string) => void;
}

export function ShareButton({
  playlistName,
  playlistUrl,
  trackCount,
  successRate,
  size = 'sm',
  variant = 'outline',
  onShare,
}: ShareButtonProps) {
  const [copied, setCopied] = useState(false);
  const [isSharing, setIsSharing] = useState(false);

  const shareText = `🎵 Just cleaned my "${playlistName}" playlist with @RadioWash! ${trackCount} tracks now family-friendly${
    successRate ? ` with ${successRate}% success rate` : ''
  }. Check it out! 🧼✨`;

  const shareUrl =
    playlistUrl || (typeof window !== 'undefined' ? window.location.href : '');

  const handleCopyLink = async () => {
    try {
      await navigator.clipboard.writeText(shareUrl);
      setCopied(true);
      setIsSharing(true);

      // toast({
      //   title: 'Link copied! 🎉',
      //   description: 'Playlist link copied to clipboard',
      // });

      setTimeout(() => {
        setCopied(false);
        setIsSharing(false);
      }, 2000);

      onShare?.('clipboard');
    } catch (err) {
      // toast({
      //   title: 'Failed to copy',
      //   description: 'Please try again',
      //   variant: 'destructive',
      // });
    }
  };

  const handleSocialShare = (platform: string) => {
    setIsSharing(true);

    let url = '';
    switch (platform) {
      case 'twitter':
        url = `https://twitter.com/intent/tweet?text=${encodeURIComponent(
          shareText
        )}&url=${encodeURIComponent(shareUrl)}`;
        break;
      case 'facebook':
        url = `https://www.facebook.com/sharer/sharer.php?u=${encodeURIComponent(
          shareUrl
        )}&quote=${encodeURIComponent(shareText)}`;
        break;
      case 'whatsapp':
        url = `https://wa.me/?text=${encodeURIComponent(
          shareText + ' ' + shareUrl
        )}`;
        break;
      case 'email':
        url = `mailto:?subject=${encodeURIComponent(
          `Check out my clean playlist: ${playlistName}`
        )}&body=${encodeURIComponent(shareText + '\n\n' + shareUrl)}`;
        break;
    }

    if (url) {
      window.open(url, '_blank', 'width=600,height=400');
    }

    // toast({
    //   title: 'Sharing your clean playlist! 🎵',
    //   description: `Opening ${platform} to share "${playlistName}"`,
    // });

    setTimeout(() => setIsSharing(false), 1500);
    onShare?.(platform);
  };

  return <div></div>;
}
