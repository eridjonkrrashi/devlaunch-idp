import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { AppDetailPage } from './pages/AppDetailPage';
import { ProjectsPage } from './pages/ProjectsPage';

function PrivateRoute({ children }: { children: React.ReactNode }) {
  const { apiKey } = useAuth();
  return apiKey ? <>{children}</> : <Navigate to="/login" replace />;
}

function AdminRoute({ children }: { children: React.ReactNode }) {
  const { isAdmin } = useAuth();
  return isAdmin ? <>{children}</> : <Navigate to="/dashboard" replace />;
}

function AppRoutes() {
  const { apiKey } = useAuth();
  return (
    <Routes>
      <Route path="/login" element={apiKey ? <Navigate to="/dashboard" replace /> : <LoginPage />} />
      <Route path="/dashboard" element={<PrivateRoute><DashboardPage /></PrivateRoute>} />
      <Route path="/apps/:name" element={<PrivateRoute><AppDetailPage /></PrivateRoute>} />
      <Route path="/projects" element={<PrivateRoute><AdminRoute><ProjectsPage /></AdminRoute></PrivateRoute>} />
      <Route path="/" element={<Navigate to={apiKey ? '/dashboard' : '/login'} replace />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </BrowserRouter>
  );
}
