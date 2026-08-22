import Link from 'next/link';

/**
 * The primary call-to-action link, used by the landing hero/nav and the
 * marketing pages. One owner for the button styling; not a client component,
 * so the static pages that use it stay fully server-rendered.
 */
export function CtaLink({
  href,
  children,
  size = 'default',
}: {
  href: string;
  children: React.ReactNode;
  size?: 'default' | 'sm';
}) {
  return (
    <Link
      href={href}
      className={
        size === 'sm'
          ? 'inline-flex items-center rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground transition-colors hover:bg-brand-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background'
          : 'inline-flex items-center rounded-md bg-primary px-5 py-2.5 text-sm font-medium text-primary-foreground transition-colors hover:bg-brand-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background'
      }
    >
      {children}
    </Link>
  );
}
