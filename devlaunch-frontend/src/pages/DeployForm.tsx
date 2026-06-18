import { useState } from 'react';
import { api, ApiError } from '../api/client';
import type { ApplicationDetailDto, ApplicationSpec } from '../api/client';
import { Plus, Trash2 } from 'lucide-react';

interface Props {
  existing?: ApplicationDetailDto;
  onSuccess: (app: ApplicationDetailDto) => void;
  onCancel: () => void;
}

export function DeployForm({ existing, onSuccess, onCancel }: Props) {
  const isEdit = !!existing;

  const [form, setForm] = useState<ApplicationSpec>({
    name: existing?.name ?? '',
    image: existing?.image ?? '',
    port: existing?.port ?? 8080,
    replicas: existing?.replicas ?? 1,
    cpuRequest: existing?.cpuRequest ?? '100m',
    cpuLimit: existing?.cpuLimit ?? '500m',
    memoryRequest: existing?.memoryRequest ?? '128Mi',
    memoryLimit: existing?.memoryLimit ?? '512Mi',
    ingressHost: existing?.ingressHost ?? '',
    hpaEnabled: existing?.hpaEnabled ?? false,
    hpaMinReplicas: existing?.hpaMinReplicas ?? 1,
    hpaMaxReplicas: existing?.hpaMaxReplicas ?? 5,
    hpaCpuTargetPercent: existing?.hpaCpuTargetPercent ?? 70,
    envVars: existing?.envVars ?? [],
  });

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const set = (key: keyof ApplicationSpec, value: unknown) =>
    setForm(prev => ({ ...prev, [key]: value }));

  const addEnvVar = () =>
    setForm(prev => ({ ...prev, envVars: [...(prev.envVars ?? []), { key: '', value: '' }] }));

  const removeEnvVar = (i: number) =>
    setForm(prev => ({ ...prev, envVars: (prev.envVars ?? []).filter((_, idx) => idx !== i) }));

  const updateEnvVar = (i: number, field: 'key' | 'value', val: string) =>
    setForm(prev => {
      const next = [...(prev.envVars ?? [])];
      next[i] = { ...next[i], [field]: val };
      return { ...prev, envVars: next };
    });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');
    try {
      const spec = { ...form, ingressHost: form.ingressHost || undefined };
      const app = isEdit
        ? await api.updateApp(existing!.name, spec)
        : await api.createApp(spec);
      onSuccess(app);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Deploy failed');
    } finally {
      setLoading(false);
    }
  };

  const field = (label: string, key: keyof ApplicationSpec, type = 'text', placeholder = '') => (
    <div>
      <label className="block text-xs font-medium text-slate-600 mb-1">{label}</label>
      <input
        type={type}
        value={String(form[key] ?? '')}
        onChange={e => set(key, type === 'number' ? Number(e.target.value) : e.target.value)}
        placeholder={placeholder}
        className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 font-mono"
        required={key === 'image'}
        disabled={key === 'name' && isEdit}
      />
    </div>
  );

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="grid grid-cols-2 gap-3">
        {field('App name *', 'name', 'text', 'my-app')}
        {field('Container image *', 'image', 'text', 'nginx:latest')}
        {field('Port', 'port', 'number', '8080')}
        {field('Replicas', 'replicas', 'number', '1')}
        {field('CPU request', 'cpuRequest', 'text', '100m')}
        {field('CPU limit', 'cpuLimit', 'text', '500m')}
        {field('Memory request', 'memoryRequest', 'text', '128Mi')}
        {field('Memory limit', 'memoryLimit', 'text', '512Mi')}
      </div>

      {field('Ingress hostname (optional)', 'ingressHost', 'text', 'app.example.com')}

      {/* Env vars */}
      <div>
        <div className="flex items-center justify-between mb-1">
          <label className="text-xs font-medium text-slate-600">Environment variables</label>
          <button type="button" onClick={addEnvVar} className="text-xs text-indigo-600 hover:text-indigo-800 flex items-center gap-0.5">
            <Plus size={12} /> Add
          </button>
        </div>
        <div className="space-y-2">
          {(form.envVars ?? []).map((ev, i) => (
            <div key={i} className="flex gap-2 items-center">
              <input
                value={ev.key}
                onChange={e => updateEnvVar(i, 'key', e.target.value)}
                placeholder="KEY"
                className="flex-1 px-2 py-1.5 border border-slate-200 rounded text-xs font-mono focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
              <input
                value={ev.value}
                onChange={e => updateEnvVar(i, 'value', e.target.value)}
                placeholder="value"
                className="flex-1 px-2 py-1.5 border border-slate-200 rounded text-xs font-mono focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
              <button type="button" onClick={() => removeEnvVar(i)} className="text-slate-400 hover:text-red-500">
                <Trash2 size={14} />
              </button>
            </div>
          ))}
        </div>
      </div>

      {/* HPA */}
      <div className="border border-slate-200 rounded-lg p-3 space-y-3">
        <label className="flex items-center gap-2 text-sm font-medium text-slate-700 cursor-pointer">
          <input
            type="checkbox"
            checked={form.hpaEnabled}
            onChange={e => set('hpaEnabled', e.target.checked)}
            className="rounded"
          />
          Enable autoscaling (HPA)
        </label>
        {form.hpaEnabled && (
          <div className="grid grid-cols-3 gap-3">
            {field('Min replicas', 'hpaMinReplicas', 'number', '1')}
            {field('Max replicas', 'hpaMaxReplicas', 'number', '10')}
            {field('CPU target %', 'hpaCpuTargetPercent', 'number', '70')}
          </div>
        )}
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 text-red-700 px-3 py-2 rounded-lg text-sm">
          {error}
        </div>
      )}

      <div className="flex gap-3 justify-end pt-2">
        <button type="button" onClick={onCancel} className="px-4 py-2 text-sm text-slate-600 hover:text-slate-900 transition-colors">
          Cancel
        </button>
        <button
          type="submit"
          disabled={loading}
          className="px-4 py-2 bg-indigo-600 hover:bg-indigo-700 disabled:bg-indigo-300 text-white text-sm font-medium rounded-lg transition-colors"
        >
          {loading ? 'Deploying…' : isEdit ? 'Update' : 'Deploy'}
        </button>
      </div>
    </form>
  );
}
