import {
  useState,
  useEffect,
  useCallback,
  type ReactNode,
} from "react";
import axios from "axios";
import { setAccessToken } from "../lib/api";
import { AuthContext, type User } from "../hooks/useAuth";

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const bootstrap = useCallback(async () => {
    try {
      const { data } = await axios.post("/api/v1/auth/refresh", null, {
        withCredentials: true,
      });
      setAccessToken(data.accessToken);
      setUser(data.user);
    } catch {
      setAccessToken(null);
      setUser(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

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
    setUser(data.user);
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
    setUser(data.user);
  };

  const logout = async () => {
    try {
      await axios.post("/api/v1/auth/logout", null, { withCredentials: true });
    } finally {
      setAccessToken(null);
      setUser(null);
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
