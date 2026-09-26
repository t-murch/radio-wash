import type { Metadata } from 'next';

import { pageOpenGraph } from '@/lib/metadata';

import { ContactForm } from './contact-form';

export const metadata: Metadata = {
  title: 'Contact',
  description:
    'Questions, problems, or ideas about RadioWash? Send a message straight to the person who builds it.',
  alternates: { canonical: './' },
  // Without this the page inherits the root layout's og:url and advertises the
  // homepage as its own canonical social URL.
  openGraph: pageOpenGraph(),
};

export default function ContactPage() {
  return (
    <article className="space-y-8">
      <header className="space-y-4">
        <h1 className="font-display text-3xl font-semibold text-foreground">
          Contact
        </h1>
        <p className="text-muted-foreground">
          Question, problem, or idea? Send a message and it goes straight to the
          person who builds RadioWash. Replies come back to the email address
          you leave here.
        </p>
      </header>

      <ContactForm />
    </article>
  );
}
