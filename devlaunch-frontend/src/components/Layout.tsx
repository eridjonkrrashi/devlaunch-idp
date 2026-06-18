import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Rocket, LayoutDashboard, FolderKanban, LogOut } from 'lucide-react';

export function Layout({ children }: { children: React.ReactNode }) {
  const { logout, isAdmin } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  const navLink = (to: string, label: string, Icon: React.ElementType) => {
    const active = location.pathname.startsWith(to);
    return (
      <Link
        to={to}
        className={`flex items-center gap-2 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
          active
            ? 'bg-indigo-700 text-white'
            : 'text-indigo-100 hover:bg-indigo-700 hover:text-white'
        }`}
      >
        <Icon size={16} />
        {label}
      </Link>
    );
  };

  return (
    <div className="min-h-screen flex bg-slate-50">
      {/* Sidebar */}
      <aside className="w-56 bg-indigo-800 flex flex-col shrink-0">
        <div className="px-4 py-5 border-b border-indigo-700">
          <div className="flex items-center gap-2">
            <Rocket size={20} className="text-indigo-200" />
            <span className="text-white font-bold text-lg">DevLaunch</span>
          </div>
          <p className="text-indigo-300 text-xs mt-0.5">Internal Developer Platform</p>
        </div>

        <nav className="flex-1 p-3 space-y-1">
          {navLink('/dashboard', 'Dashboard', LayoutDashboard)}
          {isAdmin && navLink('/projects', 'Projects', FolderKanban)}
        </nav>

        <div className="p-3 border-t border-indigo-700">
          <button
            onClick={handleLogout}
            className="flex items-center gap-2 w-full px-3 py-2 rounded-lg text-sm text-indigo-100 hover:bg-indigo-700 hover:text-white transition-colors"
          >
            <LogOut size={16} />
            Sign out
          </button>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 overflow-auto">
        <div className="max-w-6xl mx-auto px-6 py-8">
          {children}
        </div>
      </main>
    </div>
  );
}
