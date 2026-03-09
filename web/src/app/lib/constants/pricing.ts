/**
 * Centralized pricing constants for RadioWash
 * 
 * This file contains all pricing-related constants to ensure consistency
 * across the application and make price updates simple and reliable.
 */

export const SUBSCRIPTION_PRICING = {
  // Base subscription plan
  MONTHLY: {
    // The actual price charged (what Stripe charges)
    AMOUNT_CENTS: 500, // $5.00
    AMOUNT_DOLLARS: 5.00,

    // Display prices (for marketing, may be different from actual)
    DISPLAY_PRICE: '$5.00',
    MARKETING_PRICE: '$5', // Simplified for marketing copy

    // Stripe-related identifiers
    STRIPE_PRICE_ID: process.env.NEXT_PUBLIC_STRIPE_PRICE_ID_MONTHLY || '',

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
  // Remove trailing decimals for marketing copy (e.g., "$5.00" -> "$5")
  return price.replace(/\.00$/, '').replace(/\.99$/, '');
};

// Feature descriptions for UI
export const FEATURE_DESCRIPTIONS = {
  DAILY_SYNC: '⏰ Daily automatic sync',
  MAX_PLAYLISTS: (count: number) => `🎯 Up to ${count} playlists`,
  MONTHLY_PRICE: (price: string) => `💰 Only ${price}/month`,
  PRIORITY_SUPPORT: '🆘 Priority support',
} as const;

// Current active plan (easy to switch)
export const CURRENT_PLAN = SUBSCRIPTION_PRICING.MONTHLY;

/**
 * Type-safe access to pricing information
 */
export type SubscriptionPlan = typeof SUBSCRIPTION_PRICING.MONTHLY;
export type PlanFeatures = SubscriptionPlan['FEATURES'];