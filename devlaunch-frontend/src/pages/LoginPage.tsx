import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Rocket, KeyRound } from 'lucide-react';

export function LoginPage() {
  const [key, setKey] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!key.trim()) return;
    setLoading(true);
    setError('');
    try {
      await login(key.trim());
      navigate('/dashboard');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Invalid API key');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-indigo-900 via-indigo-800 to-slate-900 flex items-center justify-center px-4">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md p-8">
        <div className="flex flex-col items-center mb-8">
          <div className="bg-indigo-100 rounded-full p-4 mb-4">
            <Rocket size={32} className="text-indigo-600" />
          </div>
          <h1 className="text-2xl font-bold text-slate-900">DevLaunch IDP</h1>
          <p className="text-slate-500 text-sm mt-1">Internal Developer Platform</p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-slate-700 mb-1.5">
              <KeyRound size={14} className="inline mr-1" />
              API Key
            </label>
            <input
              type="password"
              value={key}
              onChange={e => setKey(e.target.value)}
              placeholder="dlk_..."
              className="w-full px-3 py-2.5 border border-slate-300 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent font-mono"
              autoComplete="current-password"
              required
            />
            <p className="text-xs text-slate-400 mt-1">
              Your API key is printed to the API server logs on first startup.
            </p>
          </div>

          {error && (
            <div className="bg-red-50 border border-red-200 text-red-700 px-3 py-2 rounded-lg text-sm">
              {error}
            </div>
          )}

          <button
            type="submit"
            disabled={loading || !key.trim()}
            className="w-full bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white font-medium py-2.5 px-4 rounded-lg transition-colors text-sm"
          >
            {loading ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  );
}
