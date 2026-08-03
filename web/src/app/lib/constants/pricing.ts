/**
 * Centralized pricing constants for RadioWash
 * 
 * This file contains all pricing-related constants to ensure consistency
 * across the application and make price updates simple and reliable.
 */

export const SUBSCRIPTION_PRICING = {
  // The only plan offered today. Add a tier here when one exists in Stripe — the shape is
  // kept keyed so a second tier does not require changing every call site.
  MONTHLY: {
    // The actual price charged (what Stripe charges). Must match the unit_amount of the
    // Stripe price referenced by Stripe:PricePlanId — the checkout session is created from
    // that price, so a mismatch here misstates the cost in the UI without changing the bill.
    AMOUNT_CENTS: 500, // $5.00
    AMOUNT_DOLLARS: 5.0,

    // Display prices (for marketing, may be different from actual)
    DISPLAY_PRICE: '$5.00',
    MARKETING_PRICE: '$5',
    
    // Stripe-related identifiers
    STRIPE_PRICE_ID: process.env.NEXT_PUBLIC_STRIPE_PRICE_ID || '',
    
    // Features
    FEATURES: {
      DAILY_SYNC: true,
      MAX_PLAYLISTS: 10,
      PRIORITY_SUPPORT: false,
    }
  },
} as const;

// Helper functions for formatting
export const formatPrice = (cents: number): string => {
  return `$${(cents / 100).toFixed(2)}`;
};

export const formatMarketingPrice = (price: string): string => {
  // Strip a trailing .00 or .99 for marketing copy (e.g. "$5.00" -> "$5").
  // Note this truncates rather than rounds: "$2.99" becomes "$2", not "$3". Currently unused —
  // MARKETING_PRICE is set explicitly — so the behaviour is preserved rather than corrected.
  return price.replace(/\.00$/, '').replace(/\.99$/, '');
};

// Feature descriptions for UI
export const FEATURE_DESCRIPTIONS = {
  DAILY_SYNC: '⏰ Daily automatic sync',
  MAX_PLAYLISTS: (count: number) => `🎯 Up to ${count} playlists`,
  MONTHLY_PRICE: (price: string) => `💰 Only ${price}/month`,
  PRIORITY_SUPPORT: '🆘 Priority support',
} as const;

// The plan the UI reads. Every price shown to a user resolves through here, so switching
// plans is a one-line change rather than a search across components.
export const CURRENT_PLAN = SUBSCRIPTION_PRICING.MONTHLY;

/**
 * Type-safe access to pricing information
 */
export type SubscriptionPlan = typeof SUBSCRIPTION_PRICING.MONTHLY;
export type PlanFeatures = SubscriptionPlan['FEATURES'];