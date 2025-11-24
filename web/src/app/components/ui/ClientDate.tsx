'use client';

import { useState, useEffect } from 'react';

interface ClientDateProps {
  date: string | Date;
  format?: 'toLocaleString' | 'toLocaleDateString';
  fallback?: string;
  className?: string;
}

/**
 * A component that safely renders dates on the client side only.
 * This prevents hydration mismatches caused by timezone/locale differences
 * between server and client rendering.
 */
export function ClientDate({
  date,
  format = 'toLocaleString',
  fallback = '',
  className,
}: ClientDateProps) {
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  if (!mounted) {
    return fallback ? <span className={className}>{fallback}</span> : null;
  }

  const d = new Date(date);
  const formatted =
    format === 'toLocaleDateString'
      ? d.toLocaleDateString()
      : d.toLocaleString();

  return <span className={className}>{formatted}</span>;
}
