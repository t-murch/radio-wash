import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';

import { ContactForm } from '../contact-form';
import { useBrowserSession } from '@/hooks/useBrowserSession';
import { ApiError, submitContact } from '@/services/api';

vi.mock('@/hooks/useBrowserSession', () => ({
  useBrowserSession: vi.fn(),
}));

// The real module pulls in the Supabase clients; the form only needs the two
// exports it imports. The ApiError class is redefined here so the component's
// `instanceof ApiError` check matches the errors the tests throw.
vi.mock('@/services/api', () => ({
  ApiError: class ApiError extends Error {
    constructor(
      public readonly status: number,
      message: string,
      public readonly detail?: string,
      public readonly problemType?: string
    ) {
      super(message);
      this.name = 'ApiError';
    }
  },
  submitContact: vi.fn(),
}));

const signedOutSession = { signedIn: false, email: null, displayName: null };

describe('ContactForm', () => {
  beforeEach(() => {
    (useBrowserSession as Mock).mockReturnValue(signedOutSession);
  });

  const fillAndSubmit = async (user: ReturnType<typeof userEvent.setup>) => {
    await user.type(screen.getByLabelText(/^name$/i), 'Ada Lovelace');
    await user.type(screen.getByLabelText(/email address/i), 'ada@example.com');
    await user.type(screen.getByLabelText(/^message$/i), 'The sync broke.');
    await user.click(screen.getByRole('button', { name: /send message/i }));
  };

  it('renders name, email and message fields with a submit button', () => {
    render(<ContactForm />);

    expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/email address/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^message$/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /send message/i })
    ).toBeInTheDocument();
  });

  it('prefills name and email when a session exists', () => {
    (useBrowserSession as Mock).mockReturnValue({
      signedIn: true,
      email: 'ada@example.com',
      displayName: 'Ada Lovelace',
    });

    render(<ContactForm />);

    expect(screen.getByLabelText(/^name$/i)).toHaveValue('Ada Lovelace');
    expect(screen.getByLabelText(/email address/i)).toHaveValue(
      'ada@example.com'
    );
  });

  it('stays empty and submittable when signed out', () => {
    render(<ContactForm />);

    expect(screen.getByLabelText(/^name$/i)).toHaveValue('');
    expect(screen.getByLabelText(/email address/i)).toHaveValue('');
    expect(screen.getByLabelText(/^message$/i)).toHaveValue('');
    expect(screen.getByRole('button', { name: /send message/i })).toBeEnabled();
  });

  it('submits the form and shows the success state', async () => {
    (submitContact as Mock).mockResolvedValue({ status: 'received' });
    const user = userEvent.setup();
    render(<ContactForm />);

    await fillAndSubmit(user);

    expect(await screen.findByText(/message sent/i)).toBeInTheDocument();
    expect(submitContact).toHaveBeenCalledWith({
      name: 'Ada Lovelace',
      email: 'ada@example.com',
      message: 'The sync broke.',
      website: '',
    });
  });

  it('shows the server Problem detail on ApiError (429 path)', async () => {
    (submitContact as Mock).mockRejectedValue(
      new ApiError(
        429,
        'Too many requests',
        "You've issued too many requests in a short period. Please wait a moment and try again."
      )
    );
    const user = userEvent.setup();
    render(<ContactForm />);

    await fillAndSubmit(user);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/too many requests in a short period/i);
  });

  it('includes the untouched honeypot field, hidden from assistive tech', () => {
    const { container } = render(<ContactForm />);

    const honeypot = container.querySelector('input[name="website"]');
    expect(honeypot).not.toBeNull();
    expect(honeypot).toHaveValue('');
    expect(honeypot).toHaveAttribute('tabindex', '-1');
    expect(honeypot).toHaveAttribute('autocomplete', 'off');
    // Hidden from assistive tech via an aria-hidden wrapper, not display:none,
    // so bots that skip invisible inputs still see it.
    expect(honeypot?.closest('[aria-hidden="true"]')).not.toBeNull();
  });
});
