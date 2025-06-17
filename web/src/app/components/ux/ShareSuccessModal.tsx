'use client';

import { useEffect, useState } from 'react';
// import { Dialog, DialogContent } from '@/components/ui/dialog';
// import { Button } from '@/components/ui/button';
import { Sparkles, Heart, Music, Users, Trophy } from 'lucide-react';

interface ShareSuccessModalProps {
  isOpen: boolean;
  onClose: () => void;
  playlistName: string;
  platform: string;
}

export function ShareSuccessModal({
  isOpen,
  onClose,
  playlistName,
  platform,
}: ShareSuccessModalProps) {
  const [showConfetti, setShowConfetti] = useState(false);

  useEffect(() => {
    if (isOpen) {
      setShowConfetti(true);
      const timer = setTimeout(() => setShowConfetti(false), 3000);
      return () => clearTimeout(timer);
    }
  }, [isOpen]);

  const platformEmojis: Record<string, string> = {
    twitter: '🐦',
    facebook: '📘',
    whatsapp: '💬',
    email: '📧',
    clipboard: '📋',
  };

  return <div></div>;
}
{
  /* <Dialog open={isOpen} onOpenChange={onClose}> */
}
{
  /*   <DialogContent className="sm:max-w-md text-center relative overflow-hidden"> */
}
{
  /*     {/* Animated background */
}
{
  /*     <div className="absolute inset-0 bg-gradient-to-br from-green-50 via-blue-50 to-purple-50 opacity-50" /> */
}
{
  /**/
}
{
  /*     {/* Confetti animation */
}
{
  /*     {showConfetti && ( */
}
{
  /*       <div className="absolute inset-0 pointer-events-none"> */
}
{
  /*         {[...Array(20)].map((_, i) => ( */
}
{
  /*           <div */
}
{
  /*             key={i} */
}
{
  /*             className="absolute animate-bounce" */
}
{
  /*             style={{ */
}
{
  /*               left: `${Math.random() * 100}%`, */
}
{
  /*               top: `${Math.random() * 100}%`, */
}
{
  /*               animationDelay: `${Math.random() * 2}s`, */
}
{
  /*               animationDuration: `${1 + Math.random()}s`, */
}
{
  /*             }} */
}
{
  /*           > */
}
{
  /*             {['🎵', '✨', '🎉', '💫', '🌟'][Math.floor(Math.random() * 5)]} */
}
{
  /*           </div> */
}
{
  /*         ))} */
}
{
  /*       </div> */
}
{
  /*     )} */
}
{
  /**/
}
{
  /*     <div className="relative z-10 space-y-6 py-6"> */
}
{
  /*       {/* Success icon */
}
{
  /*       <div className="mx-auto w-16 h-16 bg-green-100 rounded-full flex items-center justify-center"> */
}
{
  /*         <Trophy className="h-8 w-8 text-green-600 animate-pulse" /> */
}
{
  /*       </div> */
}
{
  /**/
}
{
  /*       {/* Success message */
}
{
  /*       <div className="space-y-2"> */
}
{
  /*         <h2 className="text-2xl font-bold text-gray-900"> */
}
{
  /*           Playlist Shared! {platformEmojis[platform] || '🎉'} */
}
{
  /*         </h2> */
}
{
  /*         <p className="text-gray-600"> */
}
{
  /*           You've shared " */
}
{
  /*           <span className="font-semibold">{playlistName}</span>" */
}
{
  /*           {platform !== 'clipboard' && ` on ${platform}`} */
}
{
  /*         </p> */
}
{
  /*       </div> */
}
{
  /**/
}
{
  /*       {/* Stats */
}
{
  /*       <div className="flex justify-center gap-6 text-sm"> */
}
{
  /*         <div className="flex items-center gap-1 text-green-600"> */
}
{
  /*           <Music className="h-4 w-4" /> */
}
{
  /*           <span>Clean Music</span> */
}
{
  /*         </div> */
}
{
  /*         <div className="flex items-center gap-1 text-blue-600"> */
}
{
  /*           <Users className="h-4 w-4" /> */
}
{
  /*           <span>Family Safe</span> */
}
{
  /*         </div> */
}
{
  /*         <div className="flex items-center gap-1 text-purple-600"> */
}
{
  /*           <Heart className="h-4 w-4" /> */
}
{
  /*           <span>Share Love</span> */
}
{
  /*         </div> */
}
{
  /*       </div> */
}
{
  /**/
}
{
  /*       {/* Encouragement message */
}
{
  /*       <div className="bg-white/80 rounded-lg p-4 border"> */
}
{
  /*         <p className="text-sm text-gray-700"> */
}
{
  /*           🎵 <strong>Spread the clean music love!</strong> Your friends and */
}
{
  /*           family will appreciate having access to great music without */
}
{
  /*           explicit content. */
}
{
  /*         </p> */
}
{
  /*       </div> */
}
{
  /**/
}
{
  /*       {/* Action button */
}
{
  /*       <Button */
}
{
  /*         onClick={onClose} */
}
{
  /*         className="w-full bg-green-600 hover:bg-green-700" */
}
{
  /*       > */
}
{
  /*         <Sparkles className="h-4 w-4 mr-2" /> */
}
{
  /*         Continue Cleaning Playlists */
}
{
  /*       </Button> */
}
{
  /*     </div> */
}
{
  /*   </DialogContent> */
}
{
  /* </Dialog> */
}
