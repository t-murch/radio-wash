import { cn } from '@/lib/utils';

function Skeleton({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      // Skeletons are decorative: they convey "loading" through the live region
      // or surrounding copy, not through their own content.
      aria-hidden="true"
      className={cn(
        'animate-pulse rounded-sm bg-muted-foreground/15 motion-reduce:animate-none',
        className
      )}
      {...props}
    />
  );
}

export { Skeleton };
