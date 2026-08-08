import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, Mock } from 'vitest';
import { useSearchParams } from 'next/navigation';

import { AuthForm } from '../auth-form';
import type { MagicLinkState } from '../actions';

vi.mock('next/navigation', () => ({
  useSearchParams: vi.fn(),
  useRouter: vi.fn(() => ({ push: vi.fn() })),
}));

// The check-inbox screen watches for a session so a link opened on another device
// moves this tab along. Default to "no session yet" so the poller stays quiet.
const onAuthStateChange = vi.fn(() => ({
  data: { subscription: { unsubscribe: vi.fn() } },
}));
const getSession = vi.fn(async () => ({ data: { session: null } }));

vi.mock('@/lib/supabase/client', () => ({
  createClient: () => ({ auth: { getSession, onAuthStateChange } }),
}));

describe('AuthForm', () => {
  let sendMagicLink: Mock;
  let signInWithApple: Mock;
  let signInWithGoogle: Mock;
  let mockSearchParams: { get: Mock };

  const renderForm = () =>
    render(
      <AuthForm
        sendMagicLink={sendMagicLink}
        signInWithApple={signInWithApple}
        signInWithGoogle={signInWithGoogle}
      />
    );

  beforeEach(() => {
    sendMagicLink = vi.fn(
      async (): Promise<MagicLinkState> => ({ status: 'idle' })
    );
    signInWithApple = vi.fn();
    signInWithGoogle = vi.fn();
    mockSearchParams = { get: vi.fn().mockReturnValue(null) };
    (useSearchParams as Mock).mockReturnValue(mockSearchParams);
    getSession.mockResolvedValue({ data: { session: null } });
  });

  it('leads with email and offers Apple and Google as alternatives', () => {
    renderForm();

    expect(
      screen.getByRole('heading', { name: /sign in with email/i })
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/email address/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /send sign-in link/i })
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /apple/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /google/i })).toBeInTheDocument();
  });

  it('offers no Spotify option', () => {
    renderForm();
    expect(screen.queryByText(/spotify/i)).not.toBeInTheDocument();
  });

  it('shows the check-inbox screen naming the address once the link is sent', async () => {
    const user = userEvent.setup();
    sendMagicLink.mockResolvedValue({
      status: 'sent',
      email: 'someone@example.com',
    } satisfies MagicLinkState);

    renderForm();
    await user.type(screen.getByLabelText(/email address/i), 'someone@example.com');
    await user.click(screen.getByRole('button', { name: /send sign-in link/i }));

    expect(
      await screen.findByRole('heading', { name: /someone@example.com/i })
    ).toBeInTheDocument();
    expect(screen.getByText(/expires after 15 minutes/i)).toBeInTheDocument();
  });

  it('lets the user go back and correct a mistyped address', async () => {
    const user = userEvent.setup();
    sendMagicLink.mockResolvedValue({
      status: 'sent',
      email: 'typo@example.com',
    } satisfies MagicLinkState);

    renderForm();
    await user.type(screen.getByLabelText(/email address/i), 'typo@example.com');
    await user.click(screen.getByRole('button', { name: /send sign-in link/i }));

    await user.click(await screen.findByRole('button', { name: /change it/i }));

    expect(
      screen.getByRole('heading', { name: /sign in with email/i })
    ).toBeInTheDocument();
  });

  it('reports a rejected address against the field itself', async () => {
    const user = userEvent.setup();
    sendMagicLink.mockResolvedValue({
      status: 'error',
      email: 'nope',
      message: "That doesn't look like an email address. Check it and retry.",
    } satisfies MagicLinkState);

    renderForm();
    await user.type(screen.getByLabelText(/email address/i), 'nope');
    await user.click(screen.getByRole('button', { name: /send sign-in link/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      /doesn't look like an email address/i
    );
    expect(screen.getByLabelText(/email address/i)).toHaveAttribute(
      'aria-invalid',
      'true'
    );
  });

  it('surfaces an error passed back through the URL', () => {
    mockSearchParams.get.mockReturnValue('We could not complete that sign-in.');
    renderForm();

    expect(screen.getByRole('alert')).toHaveTextContent(
      /could not complete that sign-in/i
    );
  });

  it('holds the resend button until the cooldown elapses', async () => {
    const user = userEvent.setup();
    sendMagicLink.mockResolvedValue({
      status: 'sent',
      email: 'someone@example.com',
    } satisfies MagicLinkState);

    renderForm();
    await user.type(screen.getByLabelText(/email address/i), 'someone@example.com');
    await user.click(screen.getByRole('button', { name: /send sign-in link/i }));

    // The countdown must not offer a resend the server would throttle.
    expect(await screen.findByText(/resend in \d+s/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /resend the link/i })
    ).not.toBeInTheDocument();
  });

  it('carries on by itself when the link is opened on another device', async () => {
    const user = userEvent.setup();
    sendMagicLink.mockResolvedValue({
      status: 'sent',
      email: 'someone@example.com',
    } satisfies MagicLinkState);
    // A session appearing is exactly what the phone opening the link looks like
    // from this tab's point of view.
    getSession.mockResolvedValue({
      data: { session: { access_token: 'signed-in' } },
    } as never);

    renderForm();
    await user.type(screen.getByLabelText(/email address/i), 'someone@example.com');
    await user.click(screen.getByRole('button', { name: /send sign-in link/i }));

    await waitFor(() =>
      expect(
        screen.getByRole('heading', { name: /signed in here too/i })
      ).toBeInTheDocument()
    );
  });
});
