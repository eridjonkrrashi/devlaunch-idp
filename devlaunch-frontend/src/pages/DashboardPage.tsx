import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { ApplicationSummaryDto } from '../api/client';
import { Layout } from '../components/Layout';
import { StatusBadge } from '../components/StatusBadge';
import { Modal } from '../components/Modal';
import { Plus, RefreshCw, Trash2, ChevronRight, Server, Activity } from 'lucide-react';
import { DeployForm } from './DeployForm';

export function DashboardPage() {
  const [apps, setApps] = useState<ApplicationSummaryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showDeploy, setShowDeploy] = useState(false);
  const [deletingApp, setDeletingApp] = useState<string | null>(null);
  const navigate = useNavigate();

  const load = useCallback(async () => {
    try {
      const data = await api.listApps();
      setApps(data);
      setError('');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to load applications');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
    const interval = setInterval(load, 15000);
    return () => clearInterval(interval);
  }, [load]);

  const handleDelete = async (name: string) => {
    if (!confirm(`Delete application "${name}"? All Kubernetes resources will be removed.`)) return;
    setDeletingApp(name);
    try {
      await api.deleteApp(name);
      setApps(prev => prev.filter(a => a.name !== name));
    } catch (e) {
      alert(e instanceof ApiError ? e.message : 'Delete failed');
    } finally {
      setDeletingApp(null);
    }
  };

  const totalRunning = apps.filter(a => a.status === 'Running').length;
  const totalFailed = apps.filter(a => a.status === 'Failed').length;

  return (
    <Layout>
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">Applications</h1>
          <p className="text-slate-500 text-sm mt-0.5">
            {apps.length} app{apps.length !== 1 ? 's' : ''} deployed
            {totalRunning > 0 && <> · <span className="text-green-600">{totalRunning} running</span></>}
            {totalFailed > 0 && <> · <span className="text-red-600">{totalFailed} failed</span></>}
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={load}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-100 text-sm transition-colors"
          >
            <RefreshCw size={14} />
            Refresh
          </button>
          <button
            onClick={() => setShowDeploy(true)}
            className="flex items-center gap-1.5 px-4 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium transition-colors"
          >
            <Plus size={14} />
            Deploy App
          </button>
        </div>
      </div>

      {/* Stats row */}
      <div className="grid grid-cols-3 gap-4 mb-6">
        {[
          { label: 'Total', value: apps.length, icon: Server, color: 'text-slate-600' },
          { label: 'Running', value: totalRunning, icon: Activity, color: 'text-green-600' },
          { label: 'Failed', value: totalFailed, icon: Activity, color: 'text-red-600' },
        ].map(({ label, value, icon: Icon, color }) => (
          <div key={label} className="bg-white border border-slate-200 rounded-xl px-5 py-4">
            <div className="flex items-center gap-2 text-slate-500 text-sm mb-1">
              <Icon size={14} className={color} />
              {label}
            </div>
            <div className={`text-2xl font-bold ${color}`}>{value}</div>
          </div>
        ))}
      </div>

      {/* Error */}
      {error && (
        <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded-xl mb-4 text-sm">
          {error}
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="bg-white border border-slate-200 rounded-xl px-6 py-12 text-center text-slate-400">
          Loading applications…
        </div>
      )}

      {/* Empty state */}
      {!loading && !error && apps.length === 0 && (
        <div className="bg-white border border-slate-200 rounded-xl px-6 py-16 text-center">
          <Server size={40} className="text-slate-300 mx-auto mb-3" />
          <p className="text-slate-500 font-medium">No applications yet</p>
          <p className="text-slate-400 text-sm mt-1 mb-4">Deploy your first app to get started.</p>
          <button
            onClick={() => setShowDeploy(true)}
            className="inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium transition-colors"
          >
            <Plus size={14} />
            Deploy App
          </button>
        </div>
      )}

      {/* App list */}
      {!loading && apps.length > 0 && (
        <div className="space-y-3">
          {apps.map(app => {
            const ready = app.liveStatus?.readyReplicas ?? 0;
            const total = app.liveStatus?.totalReplicas ?? app.replicas;
            return (
              <div
                key={app.id}
                className="bg-white border border-slate-200 rounded-xl px-5 py-4 hover:border-indigo-300 transition-colors cursor-pointer group"
                onClick={() => navigate(`/apps/${app.name}`)}
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="w-9 h-9 bg-indigo-50 rounded-lg flex items-center justify-center shrink-0">
                      <Server size={16} className="text-indigo-500" />
                    </div>
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-slate-900 font-mono text-sm">{app.name}</span>
                        <StatusBadge status={app.status} phase={app.rolloutPhase} />
                      </div>
                      <div className="text-xs text-slate-400 mt-0.5 truncate">
                        {app.image} · rev {app.currentRevision} · {ready}/{total} ready
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2 shrink-0">
                    <span className="text-xs text-slate-400 hidden sm:block">
                      {app.namespace}
                    </span>
                    <button
                      onClick={e => { e.stopPropagation(); handleDelete(app.name); }}
                      disabled={deletingApp === app.name}
                      className="p-1.5 rounded hover:bg-red-50 hover:text-red-600 text-slate-400 transition-colors opacity-0 group-hover:opacity-100"
                      title="Delete application"
                    >
                      <Trash2 size={14} />
                    </button>
                    <ChevronRight size={16} className="text-slate-300" />
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Deploy modal */}
      {showDeploy && (
        <Modal title="Deploy Application" onClose={() => setShowDeploy(false)}>
          <DeployForm
            onSuccess={app => {
              setApps(prev => [app as ApplicationSummaryDto, ...prev]);
              setShowDeploy(false);
              navigate(`/apps/${app.name}`);
            }}
            onCancel={() => setShowDeploy(false)}
          />
        </Modal>
      )}
    </Layout>
  );
}
