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
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { toast } from '@/hooks/use-toast';

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

      toast({
        title: 'Link copied! 🎉',
        description: 'Playlist link copied to clipboard',
      });

      setTimeout(() => {
        setCopied(false);
        setIsSharing(false);
      }, 2000);

      onShare?.('clipboard');
    } catch (err) {
      toast({
        title: 'Failed to copy',
        description: 'Please try again',
        variant: 'destructive',
      });
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

    toast({
      title: 'Sharing your clean playlist! 🎵',
      description: `Opening ${platform} to share "${playlistName}"`,
    });

    setTimeout(() => setIsSharing(false), 1500);
    onShare?.(platform);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant={variant}
          size={size}
          className={`relative overflow-hidden transition-all duration-300 ${
            isSharing ? 'bg-green-50 border-green-200 scale-105' : ''
          }`}
        >
          <div
            className={`flex items-center gap-2 transition-transform duration-300 ${
              isSharing ? 'scale-110' : ''
            }`}
          >
            {isSharing ? (
              <>
                <Sparkles className="h-4 w-4 text-green-600 animate-spin" />
                <span className="text-green-600 font-medium">Sharing!</span>
              </>
            ) : (
              <>
                <Share2 className="h-4 w-4" />
                <span>Share</span>
              </>
            )}
          </div>

          {/* Celebration animation overlay */}
          {isSharing && (
            <div className="absolute inset-0 bg-gradient-to-r from-green-400/20 to-blue-400/20 animate-pulse" />
          )}
        </Button>
      </DropdownMenuTrigger>

      <DropdownMenuContent align="end" className="w-56">
        <div className="px-3 py-2 border-b">
          <p className="text-sm font-medium truncate">Share "{playlistName}"</p>
          <p className="text-xs text-gray-500">{trackCount} clean tracks</p>
          {successRate && (
            <p className="text-xs text-green-600">
              {successRate}% success rate
            </p>
          )}
        </div>

        <DropdownMenuItem onClick={handleCopyLink} className="cursor-pointer">
          <div className="flex items-center gap-2 w-full">
            {copied ? (
              <Check className="h-4 w-4 text-green-600" />
            ) : (
              <Copy className="h-4 w-4" />
            )}
            <span>{copied ? 'Copied!' : 'Copy Link'}</span>
          </div>
        </DropdownMenuItem>

        <DropdownMenuSeparator />

        <DropdownMenuItem
          onClick={() => handleSocialShare('twitter')}
          className="cursor-pointer"
        >
          <Twitter className="h-4 w-4 mr-2 text-blue-500" />
          <span>Share on Twitter</span>
        </DropdownMenuItem>

        <DropdownMenuItem
          onClick={() => handleSocialShare('facebook')}
          className="cursor-pointer"
        >
          <Facebook className="h-4 w-4 mr-2 text-blue-600" />
          <span>Share on Facebook</span>
        </DropdownMenuItem>

        <DropdownMenuItem
          onClick={() => handleSocialShare('whatsapp')}
          className="cursor-pointer"
        >
          <MessageCircle className="h-4 w-4 mr-2 text-green-600" />
          <span>Share on WhatsApp</span>
        </DropdownMenuItem>

        <DropdownMenuItem
          onClick={() => handleSocialShare('email')}
          className="cursor-pointer"
        >
          <Mail className="h-4 w-4 mr-2 text-gray-600" />
          <span>Share via Email</span>
        </DropdownMenuItem>

        <DropdownMenuSeparator />

        <DropdownMenuItem
          onClick={() => window.open(shareUrl, '_blank')}
          className="cursor-pointer"
        >
          <ExternalLink className="h-4 w-4 mr-2" />
          <span>Open Playlist</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
