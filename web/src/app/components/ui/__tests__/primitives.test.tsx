import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { Alert, AlertDescription, AlertTitle } from '../alert';
import { Badge } from '../badge';
import { Card, CardContent, CardTitle } from '../card';
import { Input } from '../input';
import { Progress } from '../progress';
import { Skeleton } from '../skeleton';

describe('Alert', () => {
  it('announces errors assertively so they interrupt', () => {
    render(
      <Alert variant="error">
        <AlertTitle>Connection failed</AlertTitle>
        <AlertDescription>Try again in a moment.</AlertDescription>
      </Alert>
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Connection failed');
  });

  it('announces warnings as alerts too', () => {
    render(<Alert variant="warning">Token expires soon</Alert>);
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('announces success politely so it does not interrupt', () => {
    render(<Alert variant="success">Playlist cleaned</Alert>);

    expect(screen.getByRole('status')).toHaveTextContent('Playlist cleaned');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('treats the default variant as non-interrupting', () => {
    render(<Alert>Nothing urgent</Alert>);
    expect(screen.getByRole('status')).toBeInTheDocument();
  });
});

describe('Badge', () => {
  it('uses the semantic status token rather than a hardcoded colour', () => {
    render(<Badge variant="success">Completed</Badge>);

    const badge = screen.getByText('Completed');
    expect(badge.className).toContain('text-success');
    expect(badge.className).toContain('bg-success-muted');
  });

  it('reserves the teal primary fill for the default variant', () => {
    render(<Badge>Pro</Badge>);
    expect(screen.getByText('Pro').className).toContain('bg-primary');
  });
});

describe('Input', () => {
  it('exposes the invalid state to assistive technology', () => {
    render(<Input aria-invalid="true" defaultValue="not-an-email" />);
    expect(screen.getByRole('textbox')).toHaveAttribute('aria-invalid', 'true');
  });

  it('forwards a ref so forms can focus it', () => {
    let node: HTMLInputElement | null = null;
    render(
      <Input
        ref={(el) => {
          node = el;
        }}
      />
    );
    expect(node).toBeInstanceOf(HTMLInputElement);
  });
});

describe('Progress', () => {
  it('reports the current value to assistive technology', () => {
    render(<Progress value={42} />);

    const bar = screen.getByRole('progressbar');
    expect(bar).toHaveAttribute('aria-valuenow', '42');
  });

  it('reports an indeterminate job honestly rather than showing zero', () => {
    render(<Progress value={null} />);

    // Radix omits aria-valuenow entirely when indeterminate, which is the
    // accessible way to say "running, but no count yet".
    expect(screen.getByRole('progressbar')).not.toHaveAttribute(
      'aria-valuenow'
    );
  });
});

describe('Skeleton', () => {
  it('is hidden from assistive technology', () => {
    const { container } = render(<Skeleton className="h-4 w-32" />);
    expect(container.firstChild).toHaveAttribute('aria-hidden', 'true');
  });
});

describe('Card', () => {
  it('renders its title in the display serif', () => {
    render(
      <Card>
        <CardTitle>Road Trip</CardTitle>
        <CardContent>3 tracks</CardContent>
      </Card>
    );

    expect(screen.getByText('Road Trip').className).toContain('font-display');
  });
});
