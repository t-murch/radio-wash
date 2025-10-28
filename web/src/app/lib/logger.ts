'use client';

import * as Sentry from '@sentry/nextjs';

/**
 * Centralized logging utility for RadioWash frontend
 * 
 * Features:
 * - Environment-aware logging (debug only in development)
 * - Automatic Sentry integration for errors
 * - Data sanitization for privacy
 * - Structured logging with consistent formatting
 */

export interface LogContext {
  [key: string]: any;
}

class Logger {
  private isDevelopment: boolean;
  private isTest: boolean;

  constructor() {
    this.isDevelopment = process.env.NODE_ENV === 'development';
    this.isTest = process.env.NODE_ENV === 'test';
  }

  /**
   * Debug logging - only appears in development
   * Use for verbose debugging information
   */
  debug(message: string, context?: LogContext): void {
    if (this.isDevelopment && !this.isTest) {
      console.debug(this.formatMessage('DEBUG', message), context || '');
    }
  }

  /**
   * Info logging - appears in all environments
   * Use for important application flow information
   */
  info(message: string, context?: LogContext): void {
    if (!this.isTest) {
      console.info(this.formatMessage('INFO', message), context || '');
    }
  }

  /**
   * Warning logging - appears in all environments
   * Use for recoverable issues that need attention
   */
  warn(message: string, context?: LogContext): void {
    if (!this.isTest) {
      console.warn(this.formatMessage('WARN', message), context || '');
    }
    
    // Send warnings to Sentry in production for monitoring
    if (!this.isDevelopment) {
      Sentry.addBreadcrumb({
        message: message,
        level: 'warning',
        data: this.sanitizeContext(context),
      });
    }
  }

  /**
   * Error logging - appears in all environments
   * Automatically sends to Sentry for error tracking
   */
  error(message: string, error?: Error | unknown, context?: LogContext): void {
    if (!this.isTest) {
      console.error(this.formatMessage('ERROR', message), error || '', context || '');
    }

    // Always send errors to Sentry for tracking
    Sentry.withScope((scope) => {
      if (context) {
        scope.setContext('additional_info', this.sanitizeContext(context));
      }
      
      if (error instanceof Error) {
        scope.setTag('source', 'frontend_logger');
        Sentry.captureException(error);
      } else {
        Sentry.captureMessage(message, 'error');
      }
    });
  }

  /**
   * SignalR-specific logging helper with consistent formatting
   */
  signalR = {
    debug: (message: string, context?: LogContext) => 
      this.debug(`[SignalR] ${message}`, context),
    
    info: (message: string, context?: LogContext) => 
      this.info(`[SignalR] ${message}`, context),
    
    warn: (message: string, context?: LogContext) => 
      this.warn(`[SignalR] ${message}`, context),
    
    error: (message: string, error?: Error | unknown, context?: LogContext) => 
      this.error(`[SignalR] ${message}`, error, context),
  };

  /**
   * API-specific logging helper
   */
  api = {
    debug: (message: string, context?: LogContext) => 
      this.debug(`[API] ${message}`, context),
    
    info: (message: string, context?: LogContext) => 
      this.info(`[API] ${message}`, context),
    
    warn: (message: string, context?: LogContext) => 
      this.warn(`[API] ${message}`, context),
    
    error: (message: string, error?: Error | unknown, context?: LogContext) => 
      this.error(`[API] ${message}`, error, context),
  };

  /**
   * Auth-specific logging helper
   */
  auth = {
    debug: (message: string, context?: LogContext) => 
      this.debug(`[Auth] ${message}`, context),
    
    info: (message: string, context?: LogContext) => 
      this.info(`[Auth] ${message}`, context),
    
    warn: (message: string, context?: LogContext) => 
      this.warn(`[Auth] ${message}`, context),
    
    error: (message: string, error?: Error | unknown, context?: LogContext) => 
      this.error(`[Auth] ${message}`, error, context),
  };

  /**
   * Format log message with timestamp and level
   */
  private formatMessage(level: string, message: string): string {
    const timestamp = new Date().toISOString();
    return `[${timestamp}] ${level}: ${message}`;
  }

  /**
   * Sanitize context data to remove sensitive information
   */
  private sanitizeContext(context?: LogContext): LogContext {
    if (!context) return {};

    const sanitized: LogContext = {};
    
    for (const [key, value] of Object.entries(context)) {
      // Sanitize sensitive keys
      if (this.isSensitiveKey(key)) {
        if (typeof value === 'string') {
          sanitized[key] = this.sanitizeString(value);
        } else {
          sanitized[key] = '[REDACTED]';
        }
      } else {
        sanitized[key] = value;
      }
    }
    
    return sanitized;
  }

  /**
   * Check if a key contains sensitive information
   */
  private isSensitiveKey(key: string): boolean {
    const sensitiveKeys = [
      'token', 'accesstoken', 'refreshtoken', 'authorization',
      'password', 'secret', 'key', 'auth', 'bearer',
      'email', 'userid', 'user_id', 'connectionid'
    ];
    
    return sensitiveKeys.some(sensitiveKey => 
      key.toLowerCase().includes(sensitiveKey)
    );
  }

  /**
   * Sanitize string values (show only first/last chars for tokens, etc.)
   */
  private sanitizeString(value: string): string {
    if (value.length <= 8) {
      return '[REDACTED]';
    }
    
    // For longer strings, show first 4 and last 4 characters
    return `${value.substring(0, 4)}...${value.substring(value.length - 4)}`;
  }
}

// Export singleton instance
export const logger = new Logger();

// Export default for convenience
export default logger;