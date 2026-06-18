import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { ProjectDto, ApiKeyDto, AuditEntryDto, CreateProjectRequest, CreateApiKeyRequest } from '../api/client';
import { Layout } from '../components/Layout';
import { Modal } from '../components/Modal';
import { Plus, Trash2, Key, ClipboardList, ChevronRight, Copy, Check } from 'lucide-react';

export function ProjectsPage() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [selected, setSelected] = useState<ProjectDto | null>(null);
  const [keys, setKeys] = useState<ApiKeyDto[]>([]);
  const [audit, setAudit] = useState<AuditEntryDto[]>([]);
  const [activePanel, setActivePanel] = useState<'keys' | 'audit'>('keys');
  const [newKeyForm, setNewKeyForm] = useState<CreateApiKeyRequest>({ name: '', role: 'Developer' });
  const [createdKey, setCreatedKey] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const navigate = useNavigate();

  const load = useCallback(async () => {
    try {
      const data = await api.listProjects();
      setProjects(data);
      setError('');
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Failed to load projects');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const loadPanel = useCallback(async (p: ProjectDto, panel: 'keys' | 'audit') => {
    setSelected(p);
    setActivePanel(panel);
    if (panel === 'keys') {
      try { setKeys(await api.listApiKeys(p.id)); } catch { setKeys([]); }
    } else {
      try { setAudit(await api.getAuditLog(p.id)); } catch { setAudit([]); }
    }
  }, []);

  const handleCreateProject = async (req: CreateProjectRequest) => {
    try {
      const p = await api.createProject(req);
      setProjects(prev => [p, ...prev]);
      setShowCreate(false);
    } catch (e) {
      throw e;
    }
  };

  const handleDeleteProject = async (id: string) => {
    if (!confirm('Delete this project and all its resources?')) return;
    try {
      await api.deleteProject(id);
      setProjects(prev => prev.filter(p => p.id !== id));
      if (selected?.id === id) setSelected(null);
    } catch (e) {
      alert(e instanceof ApiError ? e.message : 'Delete failed');
    }
  };

  const handleCreateKey = async () => {
    if (!selected || !newKeyForm.name.trim()) return;
    try {
      const key = await api.createApiKey(selected.id, newKeyForm);
      if (key.rawKey) setCreatedKey(key.rawKey);
      setKeys(prev => [key, ...prev]);
      setNewKeyForm({ name: '', role: 'Developer' });
    } catch (e) {
      alert(e instanceof ApiError ? e.message : 'Failed to create key');
    }
  };

  const handleRevokeKey = async (keyId: string) => {
    if (!selected) return;
    if (!confirm('Revoke this API key? It cannot be undone.')) return;
    try {
      await api.revokeApiKey(selected.id, keyId);
      setKeys(prev => prev.map(k => k.id === keyId ? { ...k, isRevoked: true } : k));
    } catch (e) {
      alert(e instanceof ApiError ? e.message : 'Revoke failed');
    }
  };

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <Layout>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">Projects</h1>
          <p className="text-slate-500 text-sm mt-0.5">{projects.length} project{projects.length !== 1 ? 's' : ''}</p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-1.5 px-4 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium transition-colors"
        >
          <Plus size={14} /> New Project
        </button>
      </div>

      {error && <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded-xl mb-4 text-sm">{error}</div>}
      {loading && <div className="text-center text-slate-400 py-12">Loading…</div>}

      <div className="grid grid-cols-5 gap-6">
        {/* Project list */}
        <div className="col-span-2 space-y-2">
          {projects.map(p => (
            <div
              key={p.id}
              className={`bg-white border rounded-xl px-4 py-3 cursor-pointer transition-colors ${
                selected?.id === p.id ? 'border-indigo-400 ring-1 ring-indigo-200' : 'border-slate-200 hover:border-indigo-200'
              }`}
              onClick={() => navigate(`/dashboard`)} // clicking name goes to apps filtered by project
            >
              <div className="flex items-center justify-between">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-slate-900 font-mono text-sm">{p.name}</span>
                  </div>
                  <p className="text-xs text-slate-400 mt-0.5">{p.appCount} apps · ns: {p.namespace}</p>
                  {p.description && <p className="text-xs text-slate-500 mt-0.5 truncate">{p.description}</p>}
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  <button
                    onClick={e => { e.stopPropagation(); loadPanel(p, 'keys'); }}
                    className="p-1.5 rounded hover:bg-slate-100 text-slate-400 hover:text-slate-700 transition-colors"
                    title="Manage API keys"
                  >
                    <Key size={14} />
                  </button>
                  <button
                    onClick={e => { e.stopPropagation(); loadPanel(p, 'audit'); }}
                    className="p-1.5 rounded hover:bg-slate-100 text-slate-400 hover:text-slate-700 transition-colors"
                    title="View audit log"
                  >
                    <ClipboardList size={14} />
                  </button>
                  <button
                    onClick={e => { e.stopPropagation(); handleDeleteProject(p.id); }}
                    className="p-1.5 rounded hover:bg-red-50 text-slate-400 hover:text-red-600 transition-colors"
                    title="Delete project"
                  >
                    <Trash2 size={14} />
                  </button>
                  <ChevronRight size={14} className="text-slate-300" />
                </div>
              </div>
              <div className="flex gap-3 mt-2 text-xs text-slate-400">
                <span>CPU: {p.cpuQuota}</span>
                <span>Mem: {p.memoryQuota}</span>
                <span>Max apps: {p.maxApps}</span>
              </div>
            </div>
          ))}
        </div>

        {/* Side panel */}
        {selected && (
          <div className="col-span-3 bg-white border border-slate-200 rounded-xl p-5">
            <div className="flex items-center gap-3 mb-4 border-b border-slate-100 pb-3">
              <span className="font-semibold text-slate-800">{selected.name}</span>
              <div className="flex gap-2">
                {(['keys', 'audit'] as const).map(p => (
                  <button
                    key={p}
                    onClick={() => loadPanel(selected, p)}
                    className={`px-2.5 py-1 rounded text-xs font-medium transition-colors ${
                      activePanel === p ? 'bg-indigo-100 text-indigo-700' : 'text-slate-500 hover:bg-slate-100'
                    }`}
                  >
                    {p === 'keys' ? 'API Keys' : 'Audit Log'}
                  </button>
                ))}
              </div>
            </div>

            {activePanel === 'keys' && (
              <div>
                {/* Create key form */}
                <div className="flex gap-2 mb-4">
                  <input
                    value={newKeyForm.name}
                    onChange={e => setNewKeyForm(f => ({ ...f, name: e.target.value }))}
                    placeholder="Key name"
                    className="flex-1 px-2.5 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  />
                  <select
                    value={newKeyForm.role}
                    onChange={e => setNewKeyForm(f => ({ ...f, role: e.target.value as 'Admin' | 'Developer' }))}
                    className="px-2.5 py-1.5 border border-slate-200 rounded text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
                  >
                    <option value="Developer">Developer</option>
                    <option value="Admin">Admin</option>
                  </select>
                  <button
                    onClick={handleCreateKey}
                    disabled={!newKeyForm.name.trim()}
                    className="px-3 py-1.5 bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white text-sm rounded transition-colors"
                  >
                    Create
                  </button>
                </div>

                {createdKey && (
                  <div className="bg-green-50 border border-green-200 rounded-lg p-3 mb-4 text-sm">
                    <p className="text-green-800 font-medium mb-1">Key created — copy it now, it won't be shown again</p>
                    <div className="flex items-center gap-2">
                      <code className="font-mono text-xs bg-white border border-green-200 px-2 py-1 rounded flex-1 overflow-x-auto">{createdKey}</code>
                      <button onClick={() => copyToClipboard(createdKey)} className="p-1.5 rounded hover:bg-green-100 text-green-600">
                        {copied ? <Check size={14} /> : <Copy size={14} />}
                      </button>
                    </div>
                  </div>
                )}

                <div className="space-y-2">
                  {keys.filter(k => !k.isRevoked).map(k => (
                    <div key={k.id} className="flex items-center justify-between px-3 py-2 bg-slate-50 rounded-lg text-xs">
                      <div>
                        <span className="font-medium text-slate-700">{k.name}</span>
                        <span className="ml-2 px-1.5 py-0.5 bg-slate-200 text-slate-600 rounded font-mono">{k.role}</span>
                        <span className="ml-2 text-slate-400 font-mono">{k.keyPrefix}…</span>
                      </div>
                      <button
                        onClick={() => handleRevokeKey(k.id)}
                        className="text-red-500 hover:text-red-700 px-2 py-1 hover:bg-red-50 rounded transition-colors"
                      >
                        Revoke
                      </button>
                    </div>
                  ))}
                  {keys.filter(k => !k.isRevoked).length === 0 && (
                    <p className="text-slate-400 text-sm text-center py-4">No active keys</p>
                  )}
                </div>
              </div>
            )}

            {activePanel === 'audit' && (
              <div className="max-h-80 overflow-y-auto space-y-1.5">
                {audit.length === 0 && <p className="text-slate-400 text-sm text-center py-4">No audit entries</p>}
                {audit.map(entry => (
                  <div key={entry.id} className="text-xs px-3 py-2 bg-slate-50 rounded flex items-start gap-2">
                    <span className="text-slate-400 shrink-0 font-mono">{new Date(entry.timestamp).toLocaleString()}</span>
                    <span>
                      <span className="font-medium text-slate-700">{entry.action}</span>
                      {' '}<span className="text-slate-500">{entry.targetKind}/{entry.targetName}</span>
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Create project modal */}
      {showCreate && (
        <Modal title="New Project" onClose={() => setShowCreate(false)}>
          <CreateProjectForm onSubmit={handleCreateProject} onCancel={() => setShowCreate(false)} />
        </Modal>
      )}
    </Layout>
  );
}

function CreateProjectForm({ onSubmit, onCancel }: { onSubmit: (req: CreateProjectRequest) => Promise<void>; onCancel: () => void }) {
  const [form, setForm] = useState<CreateProjectRequest>({ name: '', description: '', cpuQuota: '4', memoryQuota: '8Gi', maxApps: 20 });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');
    try {
      await onSubmit(form);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to create project');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <div>
        <label className="text-xs font-medium text-slate-600 block mb-1">Project name *</label>
        <input value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} placeholder="my-team" required
          className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 font-mono" />
      </div>
      <div>
        <label className="text-xs font-medium text-slate-600 block mb-1">Description</label>
        <input value={form.description ?? ''} onChange={e => setForm(f => ({ ...f, description: e.target.value }))} placeholder="Optional"
          className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500" />
      </div>
      <div className="grid grid-cols-3 gap-3">
        <div>
          <label className="text-xs font-medium text-slate-600 block mb-1">CPU quota</label>
          <input value={form.cpuQuota} onChange={e => setForm(f => ({ ...f, cpuQuota: e.target.value }))}
            className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 font-mono" />
        </div>
        <div>
          <label className="text-xs font-medium text-slate-600 block mb-1">Memory quota</label>
          <input value={form.memoryQuota} onChange={e => setForm(f => ({ ...f, memoryQuota: e.target.value }))}
            className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 font-mono" />
        </div>
        <div>
          <label className="text-xs font-medium text-slate-600 block mb-1">Max apps</label>
          <input type="number" value={form.maxApps} onChange={e => setForm(f => ({ ...f, maxApps: Number(e.target.value) }))}
            className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500" />
        </div>
      </div>
      {error && <div className="bg-red-50 border border-red-200 text-red-700 px-3 py-2 rounded-lg text-sm">{error}</div>}
      <div className="flex gap-3 justify-end pt-2">
        <button type="button" onClick={onCancel} className="px-4 py-2 text-sm text-slate-600">Cancel</button>
        <button type="submit" disabled={loading} className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white text-sm font-medium rounded-lg">
          {loading ? 'Creating…' : 'Create Project'}
        </button>
      </div>
    </form>
  );
}
