import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { ApplicationDetailDto, RevisionDto } from '../api/client';
import { Layout } from '../components/Layout';
import { StatusBadge } from '../components/StatusBadge';
import { Modal } from '../components/Modal';
import { DeployForm } from './DeployForm';
import {
  ArrowLeft, RefreshCw, Pencil, Trash2, ChevronDown, ChevronUp,
  Scale, RotateCcw, Terminal, Radio, History,
} from 'lucide-react';

type Tab = 'overview' | 'logs' | 'events' | 'revisions';

export function AppDetailPage() {
  const { name } = useParams<{ name: string }>();
  const navigate = useNavigate();
  const [app, setApp] = useState<ApplicationDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [tab, setTab] = useState<Tab>('overview');
  const [logs, setLogs] = useState('');
  const [events, setEvents] = useState<string[]>([]);
  const [showEdit, setShowEdit] = useState(false);
  const [showScale, setShowScale] = useState(false);
  const [scaleVal, setScaleVal] = useState(1);
  const [scaling, setScaling] = useState(false);
  const [rollingBack, setRollingBack] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [revisions, setRevisions] = useState<RevisionDto[]>([]);

  const [showEnv, setShowEnv] = useState(false);

  const load = useCallback(async () => {
    if (!name) return;
    try {
      const data = await api.getApp(name);
      setApp(data);
      setScaleVal(data.replicas);
      setError('');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to load app');
    } finally {
      setLoading(false);
    }
  }, [name]);

  useEffect(() => {
    load();
    const interval = setInterval(load, 15000);
    return () => clearInterval(interval);
  }, [load]);

  const loadLogs = useCallback(async () => {
    if (!name) return;
    try { setLogs(await api.getLogs(name, 200)); } catch (e) {
      setLogs(e instanceof Error ? e.message : 'Failed to fetch logs');
    }
  }, [name]);

  const loadEvents = useCallback(async () => {
    if (!name) return;
    try { setEvents(await api.getEvents(name)); } catch { setEvents([]); }
  }, [name]);

  const loadRevisions = useCallback(async () => {
    if (!name) return;
    try { setRevisions(await api.getRevisions(name)); } catch { setRevisions([]); }
  }, [name]);

  useEffect(() => {
    if (tab === 'logs') loadLogs();
    if (tab === 'events') loadEvents();
    if (tab === 'revisions') loadRevisions();
  }, [tab, loadLogs, loadEvents, loadRevisions]);

  const handleScale = async () => {
    if (!name) return;
    setScaling(true);
    try {
      await api.scaleApp(name, scaleVal);
      await load();
      setShowScale(false);
    } catch (e) {
      alert(e instanceof ApiError ? e.message : 'Scale failed');
    } finally {
      setScaling(false);
    }
  };

  const handleRollback = async (revision?: number) => {
    if (!name) return;
    if (!confirm(`Roll back ${name} to ${revision ? `revision ${revision}` : 'previous revision'}?`)) return;
    setRollingBack(true);
    try {
      const updated = await api.rollbackApp(name, revision);
      setApp(updated);
    } catch (e) {
      alert(e instanceof ApiError ? e.message : 'Rollback failed');
    } finally {
      setRollingBack(false);
    }
  };

  const handleDelete = async () => {
    if (!name) return;
    if (!confirm(`Permanently delete "${name}" and all its Kubernetes resources?`)) return;
    setDeleting(true);
    try {
      await api.deleteApp(name);
      navigate('/dashboard');
    } catch (e) {
      alert(e instanceof ApiError ? e.message : 'Delete failed');
      setDeleting(false);
    }
  };

  if (loading) {
    return <Layout><div className="text-center text-slate-400 py-20">Loading…</div></Layout>;
  }
  if (error || !app) {
    return (
      <Layout>
        <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded-xl text-sm">{error || 'App not found'}</div>
      </Layout>
    );
  }

  const ready = app.liveStatus?.readyReplicas ?? 0;
  const total = app.liveStatus?.totalReplicas ?? app.replicas;

  const tabs: { id: Tab; label: string; icon: React.ElementType }[] = [
    { id: 'overview', label: 'Overview', icon: Radio },
    { id: 'logs', label: 'Logs', icon: Terminal },
    { id: 'events', label: 'Events', icon: Radio },
    { id: 'revisions', label: 'Revisions', icon: History },
  ];

  return (
    <Layout>
      {/* Breadcrumb */}
      <div className="flex items-center gap-2 mb-6">
        <button onClick={() => navigate('/dashboard')} className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-800 transition-colors">
          <ArrowLeft size={14} /> Apps
        </button>
        <span className="text-slate-300">/</span>
        <span className="text-slate-700 font-mono text-sm font-medium">{app.name}</span>
      </div>

      {/* Title row */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <div className="flex items-center gap-3 mb-1">
            <h1 className="text-2xl font-bold text-slate-900 font-mono">{app.name}</h1>
            <StatusBadge status={app.status} phase={app.rolloutPhase} />
          </div>
          <p className="text-sm text-slate-500">
            {app.image} · {app.namespace} · rev {app.currentRevision} · {ready}/{total} pods ready
          </p>
          {app.rolloutMessage && (
            <p className="text-xs text-yellow-700 bg-yellow-50 px-2 py-0.5 rounded mt-1 inline-block">{app.rolloutMessage}</p>
          )}
        </div>
        <div className="flex gap-2">
          <button onClick={load} className="p-2 border border-slate-200 rounded-lg text-slate-500 hover:bg-slate-50 transition-colors" title="Refresh">
            <RefreshCw size={15} />
          </button>
          <button onClick={() => setShowEdit(true)} className="flex items-center gap-1.5 px-3 py-2 border border-slate-200 rounded-lg text-slate-600 hover:bg-slate-50 text-sm transition-colors">
            <Pencil size={14} /> Update
          </button>
          <button onClick={() => setShowScale(true)} className="flex items-center gap-1.5 px-3 py-2 border border-slate-200 rounded-lg text-slate-600 hover:bg-slate-50 text-sm transition-colors">
            <Scale size={14} /> Scale
          </button>
          <button
            onClick={() => handleRollback()}
            disabled={rollingBack || app.currentRevision <= 1}
            className="flex items-center gap-1.5 px-3 py-2 border border-slate-200 rounded-lg text-slate-600 hover:bg-slate-50 disabled:opacity-40 text-sm transition-colors"
          >
            <RotateCcw size={14} /> {rollingBack ? 'Rolling back…' : 'Rollback'}
          </button>
          <button
            onClick={handleDelete}
            disabled={deleting}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-red-50 hover:bg-red-100 text-red-600 text-sm transition-colors"
          >
            <Trash2 size={14} /> {deleting ? 'Deleting…' : 'Delete'}
          </button>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex border-b border-slate-200 mb-6">
        {tabs.map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium border-b-2 transition-colors ${
              tab === id
                ? 'border-indigo-600 text-indigo-600'
                : 'border-transparent text-slate-500 hover:text-slate-800'
            }`}
          >
            <Icon size={14} /> {label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      {tab === 'overview' && (
        <div className="space-y-6">
          {/* Live status */}
          <div className="bg-white border border-slate-200 rounded-xl p-5">
            <h3 className="text-sm font-semibold text-slate-700 mb-4">Live Cluster Status</h3>
            {app.liveStatus ? (
              <>
                <div className="grid grid-cols-2 gap-4 mb-4">
                  <div>
                    <p className="text-xs text-slate-400">Ready pods</p>
                    <p className="text-xl font-bold text-slate-800">{ready} / {total}</p>
                  </div>
                  <div>
                    <p className="text-xs text-slate-400">Replicas (desired)</p>
                    <p className="text-xl font-bold text-slate-800">{app.replicas}</p>
                  </div>
                </div>
                {app.liveStatus.pods.length > 0 && (
                  <div className="border-t border-slate-100 pt-3">
                    <p className="text-xs font-medium text-slate-500 mb-2">Pods</p>
                    <div className="space-y-1">
                      {app.liveStatus.pods.map(pod => (
                        <div key={pod.name} className="flex items-center gap-2 text-xs">
                          <span className={`w-2 h-2 rounded-full shrink-0 ${pod.ready ? 'bg-green-500' : 'bg-red-400'}`} />
                          <span className="font-mono text-slate-600 truncate">{pod.name}</span>
                          <span className="text-slate-400">{pod.phase}</span>
                          {pod.restartCount && Number(pod.restartCount) > 0 && (
                            <span className="text-orange-500">↺ {pod.restartCount}</span>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </>
            ) : (
              <p className="text-slate-400 text-sm">No live status available (cluster may be unreachable)</p>
            )}
          </div>

          {/* HPA */}
          {app.hpaEnabled && (
            <div className="bg-white border border-slate-200 rounded-xl p-5">
              <h3 className="text-sm font-semibold text-slate-700 mb-3">Autoscaling (HPA)</h3>
              <div className="grid grid-cols-4 gap-4 text-sm">
                <div><p className="text-xs text-slate-400">Min</p><p className="font-medium">{app.hpaMinReplicas}</p></div>
                <div><p className="text-xs text-slate-400">Max</p><p className="font-medium">{app.hpaMaxReplicas}</p></div>
                <div><p className="text-xs text-slate-400">CPU target</p><p className="font-medium">{app.hpaCpuTargetPercent}%</p></div>
                {app.hpaStatus && (
                  <div><p className="text-xs text-slate-400">Current CPU</p><p className="font-medium">{app.hpaStatus.currentCpuPercent ?? '—'}%</p></div>
                )}
              </div>
            </div>
          )}

          {/* Config */}
          <div className="bg-white border border-slate-200 rounded-xl p-5">
            <h3 className="text-sm font-semibold text-slate-700 mb-3">Configuration</h3>
            <dl className="grid grid-cols-2 gap-x-8 gap-y-2 text-sm">
              {[
                ['Image', app.image],
                ['Port', String(app.port)],
                ['CPU request / limit', `${app.cpuRequest} / ${app.cpuLimit}`],
                ['Memory request / limit', `${app.memoryRequest} / ${app.memoryLimit}`],
                ['Namespace', app.namespace],
                app.ingressHost ? ['Ingress', app.ingressHost] : null,
              ].filter((x): x is [string, string] => x !== null).map(([label, value]) => (
                <div key={label as string}>
                  <dt className="text-slate-400">{label}</dt>
                  <dd className="font-mono text-slate-700 truncate">{value}</dd>
                </div>
              ))}
            </dl>

            {app.envVars.length > 0 && (
              <div className="mt-4 border-t border-slate-100 pt-3">
                <button
                  onClick={() => setShowEnv(v => !v)}
                  className="flex items-center gap-1.5 text-xs font-medium text-slate-500 hover:text-slate-800"
                >
                  {showEnv ? <ChevronUp size={12} /> : <ChevronDown size={12} />}
                  {app.envVars.length} env var{app.envVars.length !== 1 ? 's' : ''}
                </button>
                {showEnv && (
                  <div className="mt-2 space-y-1">
                    {app.envVars.map(ev => (
                      <div key={ev.key} className="flex gap-2 text-xs font-mono">
                        <span className="text-indigo-600 shrink-0">{ev.key}</span>
                        <span className="text-slate-400">=</span>
                        <span className="text-slate-600 truncate">{ev.value}</span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      )}

      {tab === 'logs' && (
        <div className="bg-slate-900 text-green-400 rounded-xl p-5">
          <div className="flex justify-between mb-3">
            <span className="text-xs text-slate-400">Last 200 lines · {app.name}</span>
            <button onClick={loadLogs} className="text-xs text-slate-400 hover:text-white"><RefreshCw size={12} /></button>
          </div>
          {logs ? (
            <pre className="log-output text-sm overflow-x-auto max-h-[60vh] overflow-y-auto">{logs}</pre>
          ) : (
            <p className="text-slate-500 text-sm">No logs available — pods may not be running yet.</p>
          )}
        </div>
      )}

      {tab === 'events' && (
        <div className="bg-white border border-slate-200 rounded-xl p-5">
          <div className="flex justify-between mb-3">
            <h3 className="text-sm font-semibold text-slate-700">Kubernetes Events</h3>
            <button onClick={loadEvents} className="text-xs text-slate-400 hover:text-slate-700"><RefreshCw size={12} /></button>
          </div>
          {events.length === 0 ? (
            <p className="text-slate-400 text-sm">No events found.</p>
          ) : (
            <div className="space-y-1.5 max-h-[60vh] overflow-y-auto">
              {events.map((ev, i) => (
                <div key={i} className="text-xs font-mono text-slate-600 bg-slate-50 px-3 py-1.5 rounded">{ev}</div>
              ))}
            </div>
          )}
        </div>
      )}

      {tab === 'revisions' && (
        <div className="bg-white border border-slate-200 rounded-xl p-5">
          <div className="flex justify-between mb-3">
            <h3 className="text-sm font-semibold text-slate-700">Revision History</h3>
            <button onClick={loadRevisions} className="text-xs text-slate-400 hover:text-slate-700"><RefreshCw size={12} /></button>
          </div>
          {revisions.length === 0 ? (
            <p className="text-slate-400 text-sm">No revisions found.</p>
          ) : (
            <div className="overflow-hidden rounded-lg border border-slate-100">
              <table className="w-full text-sm">
                <thead className="bg-slate-50">
                  <tr>
                    {['Rev', 'Image', 'Replicas', 'Deployed', ''].map(h => (
                      <th key={h} className="text-left text-xs font-medium text-slate-500 px-3 py-2">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {revisions.map(rev => (
                    <tr key={rev.id} className={`${rev.revisionNumber === app.currentRevision ? 'bg-indigo-50' : ''}`}>
                      <td className="px-3 py-2 font-mono text-xs text-slate-600">
                        #{rev.revisionNumber}
                        {rev.revisionNumber === app.currentRevision && (
                          <span className="ml-1.5 px-1.5 py-0.5 bg-indigo-100 text-indigo-700 rounded text-xs">current</span>
                        )}
                      </td>
                      <td className="px-3 py-2 font-mono text-xs text-slate-700">{rev.image}</td>
                      <td className="px-3 py-2 text-xs text-slate-600">{rev.replicas}</td>
                      <td className="px-3 py-2 text-xs text-slate-400">{new Date(rev.createdAt).toLocaleString()}</td>
                      <td className="px-3 py-2">
                        {rev.revisionNumber !== app.currentRevision && (
                          <button
                            onClick={() => handleRollback(rev.revisionNumber)}
                            disabled={rollingBack}
                            className="text-xs text-indigo-600 hover:text-indigo-800 disabled:opacity-40"
                          >
                            Roll back
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Scale modal */}
      {showScale && (
        <Modal title={`Scale "${app.name}"`} onClose={() => setShowScale(false)}>
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium text-slate-700 block mb-1">Replicas</label>
              <input
                type="number"
                min={1}
                max={50}
                value={scaleVal}
                onChange={e => setScaleVal(Number(e.target.value))}
                className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
              />
              {app.hpaEnabled && (
                <p className="text-xs text-yellow-600 mt-1">Note: HPA is enabled — scaling manually will disable it.</p>
              )}
            </div>
            <div className="flex gap-3 justify-end">
              <button onClick={() => setShowScale(false)} className="px-4 py-2 text-sm text-slate-600">Cancel</button>
              <button
                onClick={handleScale}
                disabled={scaling}
                className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white text-sm font-medium rounded-lg"
              >
                {scaling ? 'Scaling…' : 'Apply'}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* Edit modal */}
      {showEdit && (
        <Modal title={`Update "${app.name}"`} onClose={() => setShowEdit(false)}>
          <DeployForm
            existing={app}
            onSuccess={updated => {
              setApp(updated);
              setShowEdit(false);
            }}
            onCancel={() => setShowEdit(false)}
          />
        </Modal>
      )}
    </Layout>
  );
}
