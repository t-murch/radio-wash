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
    AMOUNT_CENTS: 299, // $2.99
    AMOUNT_DOLLARS: 2.99,
    
    // Display prices (for marketing, may be different from actual)
    DISPLAY_PRICE: '$2.99',
    MARKETING_PRICE: '$3', // Simplified for marketing copy
    
    // Stripe-related identifiers
    STRIPE_PRICE_ID: process.env.NEXT_PUBLIC_STRIPE_PRICE_ID || '',
    
    // Features
    FEATURES: {
      DAILY_SYNC: true,
      MAX_PLAYLISTS: 10,
      PRIORITY_SUPPORT: false,
    }
  },
  
  // Future pricing tiers (for easy expansion)
  YEARLY: {
    AMOUNT_CENTS: 2990, // $29.90 (about 17% discount)
    AMOUNT_DOLLARS: 29.90,
    DISPLAY_PRICE: '$29.90',
    MARKETING_PRICE: '$30',
    STRIPE_PRICE_ID: process.env.NEXT_PUBLIC_STRIPE_YEARLY_PRICE_ID || '',
    FEATURES: {
      DAILY_SYNC: true,
      MAX_PLAYLISTS: 25,
      PRIORITY_SUPPORT: true,
    }
  }
} as const;

// Helper functions for formatting
export const formatPrice = (cents: number): string => {
  return `$${(cents / 100).toFixed(2)}`;
};

export const formatMarketingPrice = (price: string): string => {
  // Remove decimals for marketing copy (e.g., "$2.99" -> "$3")
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