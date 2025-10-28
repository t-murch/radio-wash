import Image from 'next/image';
import Link from 'next/link';
import { ThemeToggle } from '../ui/theme-toggle';

export default function LandingPage() {

  return (
    <div className="min-h-screen">
      {/* Navigation */}
      <nav className="bg-card shadow-sm">
        <div className="max-w-6xl mx-auto px-4 py-4 flex justify-between items-center">
          <div className="text-2xl font-bold text-success">RadioWash</div>
          <div className="flex items-center space-x-3 sm:space-x-6">
            {/* <a */}
            {/*   href="#how-it-works" */}
            {/*   className="text-muted-foreground hover:text-foreground" */}
            {/* > */}
            {/*   How It Works */}
            {/* </a> */}
            {/* <a href="#features" className="text-muted-foreground hover:text-foreground"> */}
            {/*   Features */}
            {/* </a> */}
            {/* <a href="#faq" className="text-muted-foreground hover:text-foreground"> */}
            {/*   FAQ */}
            {/* </a> */}
            <Link
              href="/auth"
              className="bg-success text-success-foreground px-3 py-2 sm:px-4 rounded-lg hover:bg-success-hover text-sm sm:text-base inline-block"
            >
              Get Started
            </Link>
            <ThemeToggle />
          </div>
        </div>
      </nav>

      {/* Hero Section */}
      <section className="bg-gradient-to-br from-success/10 to-info/10 py-20">
        <div className="max-w-6xl mx-auto px-4 text-center">
          <h1 className="text-5xl font-bold text-foreground mb-6">
            Transform Your Explicit Playlists into
            <span className="text-success"> Clean Versions</span>
          </h1>
          <p className="text-xl text-muted-foreground mb-8 max-w-3xl mx-auto">
            Automatically find clean alternatives for explicit tracks in your
            Spotify playlists. Perfect for family listening, work environments,
            or personal preference.
          </p>
          <Link
            href="/auth"
            className="bg-success text-success-foreground px-8 py-4 rounded-lg text-lg font-semibold hover:bg-success-hover transition-colors inline-block"
          >
            Connect with Spotify - It&apos;s Free
          </Link>
          <p className="text-sm text-muted-foreground mt-4">
            No credit card required • 30 seconds to start
          </p>
        </div>
      </section>

      {/* Problem Section */}
      <section className="py-16 bg-card">
        <div className="max-w-4xl mx-auto px-4 text-center">
          <h2 className="text-3xl font-bold mb-8">
            The Problem with Explicit Content
          </h2>
          <div className="grid md:grid-cols-3 gap-8">
            <div className="p-6">
              <div className="text-4xl mb-4">👨‍👩‍👧‍👦</div>
              <h3 className="font-semibold mb-2">Family Listening</h3>
              <p className="text-muted-foreground">
                Your favorite songs aren&apos;t always appropriate for kids
              </p>
            </div>
            <div className="p-6">
              <div className="text-4xl mb-4">🏢</div>
              <h3 className="font-semibold mb-2">Work Environment</h3>
              <p className="text-muted-foreground">
                Professional settings require clean content
              </p>
            </div>
            <div className="p-6">
              <div className="text-4xl mb-4">🎧</div>
              <h3 className="font-semibold mb-2">Personal Preference</h3>
              <p className="text-muted-foreground">
                Sometimes you just want the music without the language
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Before/After Screenshots */}
      <section className="py-16 bg-background">
        <div className="max-w-6xl mx-auto px-4">
          <h2 className="text-3xl font-bold text-center mb-12">
            See the Magic in Action
          </h2>

          <div className="grid md:grid-cols-2 gap-8 items-center">
            <div>
              <h3 className="text-xl font-semibold mb-4 text-error">
                Before: Original Playlist
              </h3>
              <div className="border rounded-lg overflow-hidden shadow-lg bg-card p-4">
                <div className="space-y-3">
                  <div className="flex items-center justify-between p-2 bg-error-muted rounded">
                    <span>Track with explicit lyrics</span>
                    <span className="text-error text-sm">🚫 Explicit</span>
                  </div>
                  <div className="flex items-center justify-between p-2 bg-muted rounded">
                    <span>Clean track</span>
                    <span className="text-success text-sm">✓ Clean</span>
                  </div>
                  <div className="flex items-center justify-between p-2 bg-error-muted rounded">
                    <span>Another explicit track</span>
                    <span className="text-error text-sm">🚫 Explicit</span>
                  </div>
                </div>
              </div>
              <p className="text-sm text-muted-foreground mt-2">
                ❌ 15 explicit tracks found
              </p>
            </div>

            <div>
              <h3 className="text-xl font-semibold mb-4 text-success">
                After: Clean Version
              </h3>
              <div className="border rounded-lg overflow-hidden shadow-lg bg-card p-4">
                <div className="space-y-3">
                  <div className="flex items-center justify-between p-2 bg-success-muted rounded">
                    <span>Clean alternative found</span>
                    <span className="text-success text-sm">✓ Clean</span>
                  </div>
                  <div className="flex items-center justify-between p-2 bg-success-muted rounded">
                    <span>Same clean track</span>
                    <span className="text-success text-sm">✓ Clean</span>
                  </div>
                  <div className="flex items-center justify-between p-2 bg-success-muted rounded">
                    <span>Clean version found</span>
                    <span className="text-success text-sm">✓ Clean</span>
                  </div>
                </div>
              </div>
              <p className="text-sm text-muted-foreground mt-2">
                ✅ 13 clean alternatives found (87% success rate)
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* How It Works */}
      {/* <section id="how-it-works" className="py-16 bg-card"> */}
      {/*   <div className="max-w-6xl mx-auto px-4"> */}
      {/*     <h2 className="text-3xl font-bold text-center mb-12">How It Works</h2> */}
      {/**/}
      {/*     <div className="grid md:grid-cols-3 gap-8"> */}
      {/*       {[ */}
      {/*         { */}
      {/*           step: 1, */}
      {/*           title: 'Connect Your Spotify', */}
      {/*           url: '/screenshots/test-signup.png', */}
      {/*           description: */}
      {/*             'Securely connect your Spotify account with one click', */}
      {/*         }, */}
      {/*         { */}
      {/*           step: 2, */}
      {/*           title: 'Select Playlist', */}
      {/*           url: '/screenshots/test01.png', */}
      {/*           description: */}
      {/*             'Choose any playlist you want to clean from your library', */}
      {/*         }, */}
      {/*         { */}
      {/*           step: 3, */}
      {/*           title: 'Get Clean Version', */}
      {/*           url: '/screenshots/test02.png', */}
      {/*           description: */}
      {/*             'We create a new playlist with clean alternatives automatically', */}
      {/*         }, */}
      {/*       ].map((item) => ( */}
      {/*         <div key={item.step} className="text-center"> */}
      {/*           <div className="bg-green-100 text-green-600 rounded-full w-12 h-12 flex items-center justify-center mx-auto mb-4 text-xl font-bold"> */}
      {/*             {item.step} */}
      {/*           </div> */}
      {/*           <h3 className="text-xl font-semibold mb-3">{item.title}</h3> */}
      {/*           <div className="border rounded-lg overflow-hidden shadow-md mb-4 bg-muted h-40 flex items-center justify-center"> */}
      {/*             <Image src={item.url} alt={''} width={352} height={160} /> */}
      {/*           </div> */}
      {/*           <p className="text-muted-foreground">{item.description}</p> */}
      {/*         </div> */}
      {/*       ))} */}
      {/*     </div> */}
      {/*   </div> */}
      {/* </section> */}

      {/* Features */}
      <section id="features" className="py-16 bg-background">
        <div className="max-w-6xl mx-auto px-4">
          <h2 className="text-3xl font-bold text-center mb-12">
            Why Choose RadioWash?
          </h2>

          <div className="grid md:grid-cols-2 lg:grid-cols-3 gap-8">
            {[
              {
                icon: '🤖',
                title: 'AI-Powered Matching',
                description:
                  'Smart algorithm finds the best clean alternatives',
              },
              {
                icon: '⚡',
                title: 'Lightning Fast',
                description: 'Process entire playlists in minutes, not hours',
              },
              {
                icon: '🔒',
                title: 'Secure & Private',
                description:
                  'Your data stays safe. We only access what we need',
              },
              {
                icon: '💯',
                title: 'High Success Rate',
                description: 'Find clean versions for 80%+ of explicit tracks',
              },
              {
                icon: '🎵',
                title: 'Preserves Quality',
                description: 'Keep the same artists and song quality you love',
              },
              {
                icon: '🆓',
                title: 'Completely Free',
                description: 'No hidden costs, no subscriptions required',
              },
            ].map((feature, index) => (
              <div
                key={index}
                className="bg-card p-6 rounded-lg shadow text-center"
              >
                <div className="text-3xl mb-4">{feature.icon}</div>
                <h3 className="font-semibold mb-2">{feature.title}</h3>
                <p className="text-muted-foreground text-sm">
                  {feature.description}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Social Proof */}
      <section className="py-16 bg-success/10">
        <div className="max-w-4xl mx-auto px-4">
          <h2 className="text-3xl font-bold text-center mb-12">
            What Users Are Saying
          </h2>
          <div className="grid md:grid-cols-2 gap-8">
            <div className="bg-card p-6 rounded-lg shadow">
              <p className="mb-4">
                &quot;Finally! I can play my hip-hop playlist during family
                dinner without worrying about explicit lyrics.&quot;
              </p>
              <div className="flex items-center">
                <Image
                  src={'/user.svg'}
                  alt="user"
                  height={20}
                  width={20}
                  className="w-10 h-10 bg-muted rounded-full mr-3"
                />
                <div>
                  <p className="font-semibold">Sarah M.</p>
                  <p className="text-sm text-muted-foreground">Parent of 2</p>
                </div>
              </div>
            </div>
            <div className="bg-card p-6 rounded-lg shadow">
              <p className="mb-4">
                &quot;Perfect for our office playlist. Now everyone can enjoy
                the music without any awkward moments.&quot;
              </p>
              <div className="flex items-center">
                <Image
                  src={'/user.svg'}
                  alt="user"
                  height={20}
                  width={20}
                  className="w-10 h-10 bg-muted rounded-full mr-3"
                />
                <div>
                  <p className="font-semibold">Mike T.</p>
                  <p className="text-sm text-muted-foreground">
                    Office Manager
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* FAQ */}
      <section id="faq" className="py-16 bg-card">
        <div className="max-w-4xl mx-auto px-4">
          <h2 className="text-3xl font-bold text-center mb-12">
            Frequently Asked Questions
          </h2>
          <div className="space-y-6">
            {[
              {
                question: 'How does RadioWash find clean alternatives?',
                answer:
                  "We use advanced matching algorithms to search Spotify's catalog for clean versions of the same songs by the same artists, or similar tracks with clean lyrics.",
              },
              {
                question: 'Is my Spotify data safe?',
                answer:
                  'Absolutely. We only access your playlists to read track information and create new clean playlists. We never store your personal data or modify your existing playlists.',
              },
              {
                question: 'What if no clean version exists?',
                answer:
                  "If we can't find a clean alternative, the track simply won't be included in the new playlist. You'll see a detailed report of what was and wasn't matched.",
              },
              {
                question: 'Does this work with all Spotify accounts?',
                answer:
                  'Yes! RadioWash works with both free and premium Spotify accounts.',
              },
            ].map((faq, index) => (
              <div key={index} className="border-b pb-4">
                <h3 className="font-semibold mb-2">{faq.question}</h3>
                <p className="text-muted-foreground">{faq.answer}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* CTA */}
      <section className="py-16 bg-gradient-to-r from-success to-info text-primary-foreground">
        <div className="max-w-4xl mx-auto px-4 text-center">
          <h2 className="text-3xl font-bold mb-4">
            Ready to Clean Your Playlists?
          </h2>
          <p className="text-xl mb-8 opacity-90">
            Join thousands of users who&apos;ve made their music family-friendly
          </p>
          <Link
            href="/auth"
            className="bg-card text-success px-8 py-4 rounded-lg text-lg font-semibold hover:bg-muted transition-colors inline-block"
          >
            Get Started for Free
          </Link>
        </div>
      </section>

      {/* Footer */}
      <footer className="bg-card border-t py-8">
        <div className="max-w-6xl mx-auto px-4 text-center">
          <div className="mb-4">
            <span className="text-xl font-bold text-success">RadioWash</span>
          </div>
          <p className="text-muted-foreground mb-4">
            Making music safe for everyone, everywhere.
          </p>
          {/* <div className="space-x-6 text-sm"> */}
          {/*   <a href="/privacy" className="hover:text-gray-300"> */}
          {/*     Privacy Policy */}
          {/*   </a> */}
          {/*   <a href="/terms" className="hover:text-gray-300"> */}
          {/*     Terms of Service */}
          {/*   </a> */}
          {/*   <a href="/contact" className="hover:text-gray-300"> */}
          {/*     Contact */}
          {/*   </a> */}
          {/* </div> */}
        </div>
      </footer>
    </div>
  );
}
