import {
  useState,
  useEffect,
  useCallback,
  type ReactNode,
} from "react";
import axios from "axios";
import { setAccessToken, refreshSession } from "../lib/api";
import { AuthContext, type User } from "../hooks/useAuth";

// We cache only the (non-sensitive) user profile in sessionStorage so the app knows *who* is
// signed in instantly on a hard reload — the routing decision and app shell render without waiting
// on the async /auth/refresh round-trip. The JWT itself is NEVER persisted (memory only, per
// CLAUDE.md); a fresh access token is still obtained via the httpOnly refresh cookie on bootstrap.
const USER_CACHE_KEY = "flowpilot.user";

function readCachedUser(): User | null {
  try {
    const raw = sessionStorage.getItem(USER_CACHE_KEY);
    return raw ? (JSON.parse(raw) as User) : null;
  } catch {
    return null;
  }
}

function writeCachedUser(user: User | null) {
  try {
    if (user) sessionStorage.setItem(USER_CACHE_KEY, JSON.stringify(user));
    else sessionStorage.removeItem(USER_CACHE_KEY);
  } catch {
    // sessionStorage may be unavailable (private mode quota) — non-fatal.
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(() => readCachedUser());
  // If we already know who the user is from the cache, skip the blocking loading state so
  // ProtectedRoute renders the app immediately instead of a full-screen spinner.
  const [isLoading, setIsLoading] = useState(() => readCachedUser() === null);

  const applyUser = useCallback((next: User | null) => {
    setUser(next);
    writeCachedUser(next);
  }, []);

  const bootstrap = useCallback(async () => {
    try {
      // Shares the single-flight refresh with the axios 401 interceptor, so an optimistic render
      // that fires queries before the token is set doesn't spawn a second /auth/refresh.
      const session = await refreshSession();
      applyUser(session.user);
    } catch {
      setAccessToken(null);
      applyUser(null);
    } finally {
      setIsLoading(false);
    }
  }, [applyUser]);

  useEffect(() => {
    bootstrap();
  }, [bootstrap]);

  const login = async (email: string, password: string) => {
    const { data } = await axios.post(
      "/api/v1/auth/login",
      { email, password },
      { withCredentials: true }
    );
    setAccessToken(data.accessToken);
    applyUser(data.user);
  };

  const register = async (params: {
    email: string;
    password: string;
    firstName: string;
    lastName: string;
    businessName: string;
  }) => {
    const { data } = await axios.post(
      "/api/v1/auth/register",
      params,
      { withCredentials: true }
    );
    setAccessToken(data.accessToken);
    applyUser(data.user);
  };

  const logout = async () => {
    try {
      await axios.post("/api/v1/auth/logout", null, { withCredentials: true });
    } finally {
      setAccessToken(null);
      applyUser(null);
    }
  };

  return (
    <AuthContext.Provider
      value={{ user, isAuthenticated: !!user, isLoading, login, register, logout }}
    >
      {children}
    </AuthContext.Provider>
  );
}
