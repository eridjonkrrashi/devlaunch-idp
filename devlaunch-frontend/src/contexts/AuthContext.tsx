import { createContext, useContext, useState, useCallback, type ReactNode } from 'react';
import { api, ApiError, clearStoredApiKey, getStoredApiKey, setStoredApiKey } from '../api/client';

interface AuthContextValue {
  apiKey: string | null;
  isAdmin: boolean;
  login: (key: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [apiKey, setApiKey] = useState<string | null>(getStoredApiKey);
  // Admin detection: bootstrap key starts with dlk_ and we detect it after projects list
  const [isAdmin, setIsAdmin] = useState(false);

  const login = useCallback(async (key: string) => {
    setStoredApiKey(key);
    setApiKey(key);
    try {
      const projects = await api.listProjects();
      // Admins can see multiple projects or the result will reveal role via project count
      setIsAdmin(projects.length >= 0); // always true — role determined by API responses
      // We detect admin by trying to fetch all projects successfully with >1 result
      // or by receiving non-403 on admin endpoints; treat as admin if multiple projects visible
      setIsAdmin(projects.length > 1 || key.startsWith('dlk_'));
    } catch (e) {
      clearStoredApiKey();
      setApiKey(null);
      throw e instanceof ApiError ? e : new ApiError(401, 'Login failed');
    }
  }, []);

  const logout = useCallback(() => {
    clearStoredApiKey();
    setApiKey(null);
    setIsAdmin(false);
  }, []);

  return (
    <AuthContext.Provider value={{ apiKey, isAdmin, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
