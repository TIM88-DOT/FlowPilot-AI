import axios, { type AxiosError } from "axios";
import { toast } from "sonner";

let accessToken: string | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function getAccessToken() {
  return accessToken;
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

let isRefreshing = false;
let refreshQueue: Array<{
  resolve: (token: string) => void;
  reject: (err: unknown) => void;
}> = [];

function processQueue(error: unknown, token: string | null) {
  refreshQueue.forEach((p) => {
    if (error) p.reject(error);
    else p.resolve(token!);
  });
  refreshQueue = [];
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

    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        refreshQueue.push({
          resolve: (token: string) => {
            original.headers.Authorization = `Bearer ${token}`;
            resolve(api(original));
          },
          reject,
        });
      });
    }

    original._retry = true;
    isRefreshing = true;

    try {
      const { data } = await axios.post("/api/v1/auth/refresh", null, {
        withCredentials: true,
      });
      const newToken = data.accessToken;
      setAccessToken(newToken);
      processQueue(null, newToken);
      original.headers.Authorization = `Bearer ${newToken}`;
      return api(original);
    } catch (err) {
      processQueue(err, null);
      setAccessToken(null);
      window.location.href = "/login";
      return Promise.reject(err);
    } finally {
      isRefreshing = false;
    }
  }
);

export default api;
