import axios, { type AxiosError } from "axios";
import { toast } from "sonner";
import type { User } from "../hooks/useAuth";

/** Shape returned by POST /auth/refresh (and /auth/login, /auth/register). */
export interface AuthSession {
  accessToken: string;
  user: User;
}

let accessToken: string | null = null;
const tokenListeners = new Set<() => void>();

export function setAccessToken(token: string | null) {
  accessToken = token;
  tokenListeners.forEach((listener) => listener());
}

export function getAccessToken() {
  return accessToken;
}

/**
 * Subscribe to access-token changes. Used by `useSyncExternalStore` consumers so React can react
 * to the in-memory token becoming available/cleared (e.g. gating the SignalR connection until a
 * valid token exists). Returns an unsubscribe function.
 */
export function subscribeAccessToken(listener: () => void): () => void {
  tokenListeners.add(listener);
  return () => tokenListeners.delete(listener);
}

const api = axios.create({
  baseURL: "/api/v1",
  withCredentials: true,
});

api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }
  return config;
});

/** Extracts a user-friendly message from an API error response. */
export function extractErrorMessage(error: AxiosError<{ description?: string; title?: string; detail?: string }>): string {
  const data = error.response?.data;
  if (data?.description) return data.description;
  if (data?.detail) return data.detail;
  if (data?.title) return data.title;

  const status = error.response?.status;
  if (status === 403) return "You don't have permission to do that.";
  if (status === 404) return "The requested resource was not found.";
  if (status === 409) return "A conflict occurred. The item may already exist.";
  if (status === 422) return "The submitted data is invalid.";
  if (status && status >= 500) return "A server error occurred. Please try again.";

  return "Something went wrong. Please try again.";
}

let refreshPromise: Promise<AuthSession> | null = null;

/**
 * Single-flight refresh of the access token. Concurrent callers — the AuthProvider bootstrap and
 * any number of 401 response-interceptor retries — share one in-flight POST /auth/refresh rather
 * than each firing their own (which previously produced a burst of duplicate, often-aborted
 * refresh requests on every page load). Sets the new in-memory access token on success.
 *
 * Uses the bare `axios` (not the `api` instance) so a 401 on the refresh endpoint itself does NOT
 * re-enter this interceptor and loop.
 */
export function refreshSession(): Promise<AuthSession> {
  refreshPromise ??= axios
    .post<AuthSession>("/api/v1/auth/refresh", null, { withCredentials: true })
    .then((res) => {
      setAccessToken(res.data.accessToken);
      return res.data;
    })
    .finally(() => {
      refreshPromise = null;
    });
  return refreshPromise;
}

api.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config;

    if (error.response?.status !== 401 || original._retry) {
      // Show toast for server/client errors (skip 401 which is handled below)
      if (error.response?.status && error.response.status !== 401) {
        toast.error(extractErrorMessage(error));
      } else if (!error.response) {
        toast.error("Network error. Check your connection.");
      }
      return Promise.reject(error);
    }

    original._retry = true;

    try {
      const { accessToken: newToken } = await refreshSession();
      original.headers.Authorization = `Bearer ${newToken}`;
      return api(original);
    } catch (err) {
      setAccessToken(null);
      window.location.href = "/login";
      return Promise.reject(err);
    }
  }
);

export default api;
