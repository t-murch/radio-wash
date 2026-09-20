'use client';

import { useEffect, useState, useTransition } from 'react';
import { Loader2 } from 'lucide-react';

import { useBrowserSession } from '@/hooks/useBrowserSession';
import { ApiError, submitContact } from '@/services/api';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';

const MESSAGE_MAX_LENGTH = 5000;

export function ContactForm() {
  const { email: sessionEmail, displayName } = useBrowserSession();

  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [message, setMessage] = useState('');
  const [status, setStatus] = useState<'idle' | 'sent'>('idle');
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  // The session arrives a beat after the static HTML renders (see
  // useBrowserSession). Prefill only fields the visitor hasn't typed in, so a
  // late-arriving session never overwrites their input.
  useEffect(() => {
    if (displayName) {
      setName((current) => (current === '' ? displayName : current));
    }
    if (sessionEmail) {
      setEmail((current) => (current === '' ? sessionEmail : current));
    }
  }, [displayName, sessionEmail]);

  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    // The honeypot is uncontrolled on purpose: a real visitor never sees or
    // touches it, so its value comes straight off the form.
    const website = String(new FormData(event.currentTarget).get('website') ?? '');

    const trimmedName = name.trim();
    const trimmedEmail = email.trim();
    const trimmedMessage = message.trim();

    if (!trimmedName || !trimmedEmail || !trimmedMessage) {
      setError('Please fill in your name, email, and message.');
      return;
    }
    if (trimmedMessage.length > MESSAGE_MAX_LENGTH) {
      setError(
        `Your message is a little long — the limit is ${MESSAGE_MAX_LENGTH.toLocaleString()} characters.`
      );
      return;
    }

    setError(null);
    startTransition(async () => {
      try {
        await submitContact({
          name: trimmedName,
          email: trimmedEmail,
          message: trimmedMessage,
          website,
        });
        setStatus('sent');
      } catch (submitError) {
        setError(
          submitError instanceof ApiError
            ? (submitError.detail ?? submitError.message)
            : 'Something went wrong sending your message. Please try again.'
        );
      }
    });
  };

  if (status === 'sent') {
    return (
      <div className="space-y-4" role="status">
        <h2 className="font-display text-xl font-semibold text-foreground">
          Message sent
        </h2>
        <p className="text-muted-foreground">
          <span>Thanks for reaching out. Replies go to </span>
          {/* The address must survive machine translation verbatim. */}
          <span translate="no" className="notranslate text-foreground">
            {email}
          </span>
          <span>.</span>
        </p>
        <Button
          type="button"
          variant="outline"
          onClick={() => {
            setStatus('idle');
            setMessage('');
          }}
        >
          Send another message
        </Button>
      </div>
    );
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4" noValidate>
      <div className="space-y-2">
        <Label htmlFor="contact-name">Name</Label>
        <Input
          id="contact-name"
          name="name"
          autoComplete="name"
          required
          placeholder="Your name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          aria-invalid={error ? 'true' : undefined}
          aria-describedby={error ? 'contact-error' : undefined}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="contact-email">Email address</Label>
        <Input
          id="contact-email"
          name="email"
          type="email"
          autoComplete="email"
          required
          placeholder="you@example.com"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          aria-invalid={error ? 'true' : undefined}
          aria-describedby={error ? 'contact-error' : undefined}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="contact-message">Message</Label>
        <Textarea
          id="contact-message"
          name="message"
          required
          maxLength={MESSAGE_MAX_LENGTH}
          placeholder="What's on your mind?"
          value={message}
          onChange={(event) => setMessage(event.target.value)}
          aria-invalid={error ? 'true' : undefined}
          aria-describedby={error ? 'contact-error' : undefined}
        />
      </div>

      {/*
        Honeypot: invisible to people (moved off-screen, skipped by tab order,
        hidden from assistive tech), but present in the DOM for bots that fill
        every field. The API silently drops any submission that carries a value
        here. display:none is avoided because some bots skip hidden inputs.
      */}
      <div aria-hidden="true" className="absolute -left-[9999px] top-auto">
        <label htmlFor="contact-website">Website</label>
        <input
          id="contact-website"
          name="website"
          type="text"
          tabIndex={-1}
          autoComplete="off"
          defaultValue=""
        />
      </div>

      {error && (
        <p id="contact-error" role="alert" className="text-sm text-error">
          <span>{error}</span>
        </p>
      )}

      <Button type="submit" disabled={isPending}>
        {isPending && <Loader2 className="size-4 animate-spin" />}
        {/* Bare text kept inside a span: Chrome Translate rewraps loose text
            nodes in <font> tags, and React then crashes inserting the spinner
            next to a text node it no longer owns (Sentry JAVASCRIPT-NEXTJS-22). */}
        <span>{isPending ? 'Sending…' : 'Send message'}</span>
      </Button>
    </form>
  );
}
