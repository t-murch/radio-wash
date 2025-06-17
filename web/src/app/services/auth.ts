class AuthManager {
  private token: string | null = null;
  private refreshPromise: Promise<string> | null = null;

  async getToken(): Promise<string | null> {
    // First check in-memory token
    if (this.token && !this.isTokenExpired(this.token)) {
      return this.token;
    }

    // Try to refresh if we don't have a valid token
    if (this.refreshPromise) {
      return this.refreshPromise;
    }

    this.refreshPromise = this.refreshToken();
    try {
      this.token = await this.refreshPromise;
      return this.token;
    } catch (error) {
      this.token = null;
      throw error;
    } finally {
      this.refreshPromise = null;
    }
  }

  async setToken(token: string): Promise<void> {
    this.token = token;
  }

  async refreshToken(): Promise<string> {
    const response = await fetch('/api/auth/refresh', {
      method: 'POST',
      credentials: 'include', // Include httpOnly cookies
    });

    if (!response.ok) {
      throw new Error('Failed to refresh token');
    }

    const { token } = await response.json();
    return token;
  }

  logout() {
    this.token = null;
    // Clear server-side session
    fetch('/api/auth/logout', {
      method: 'POST',
      credentials: 'include',
    }).catch(() => {
      // Don't block logout on network errors
    });
  }

  private isTokenExpired(token: string): boolean {
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      // Add 30 second buffer for clock skew
      return payload.exp * 1000 < Date.now() + 30000;
    } catch {
      return true;
    }
  }
}

export const authManager = new AuthManager();
