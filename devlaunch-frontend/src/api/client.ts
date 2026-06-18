const KEY_STORAGE = 'devlaunch_api_key';

export function getStoredApiKey(): string | null {
  return localStorage.getItem(KEY_STORAGE);
}

export function setStoredApiKey(key: string) {
  localStorage.setItem(KEY_STORAGE, key);
}

export function clearStoredApiKey() {
  localStorage.removeItem(KEY_STORAGE);
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const key = getStoredApiKey();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(init.headers as Record<string, string>),
  };
  if (key) headers['X-API-Key'] = key;

  const res = await fetch(`/api${path}`, { ...init, headers });

  if (res.status === 204) return undefined as T;

  const text = await res.text();
  let body: unknown;
  try { body = JSON.parse(text); } catch { body = text; }

  if (!res.ok) {
    const err = (body as { error?: string; message?: string }) ?? {};
    throw new ApiError(res.status, err.error ?? err.message ?? String(body));
  }
  return body as T;
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

// ── Types (mirrors server DTOs) ────────────────────────────────────────────

export interface ProjectDto {
  id: string;
  name: string;
  namespace: string;
  description?: string;
  cpuQuota: string;
  memoryQuota: string;
  maxApps: number;
  appCount: number;
  createdAt: string;
}

export interface CreateProjectRequest {
  name: string;
  description?: string;
  cpuQuota?: string;
  memoryQuota?: string;
  maxApps?: number;
}

export interface ApiKeyDto {
  id: string;
  projectId: string;
  keyPrefix: string;
  name: string;
  role: string;
  isRevoked: boolean;
  createdAt: string;
  lastUsedAt?: string;
  rawKey?: string;
}

export interface CreateApiKeyRequest {
  name: string;
  role: 'Admin' | 'Developer';
}

export interface AuditEntryDto {
  id: string;
  action: string;
  targetKind: string;
  targetName: string;
  actorKeyPrefix?: string;
  details?: string;
  timestamp: string;
}

export interface LiveStatusDto {
  readyReplicas: number;
  totalReplicas: number;
  pods: PodStatusDto[];
  conditions: string[];
}

export interface PodStatusDto {
  name: string;
  phase: string;
  ready: boolean;
  restartCount?: string;
}

export interface HpaStatusDto {
  currentReplicas: number;
  desiredReplicas: number;
  currentCpuPercent?: number;
  targetCpuPercent: number;
}

export interface RevisionDto {
  id: string;
  revisionNumber: number;
  image: string;
  replicas: number;
  createdAt: string;
}

export interface ApplicationSummaryDto {
  id: string;
  name: string;
  namespace: string;
  image: string;
  port: number;
  replicas: number;
  status: string;
  rolloutPhase: string;
  rolloutMessage?: string;
  currentRevision: number;
  createdAt: string;
  updatedAt: string;
  liveStatus?: LiveStatusDto;
}

export interface ApplicationDetailDto extends ApplicationSummaryDto {
  envVars: { key: string; value: string }[];
  cpuRequest: string;
  cpuLimit: string;
  memoryRequest: string;
  memoryLimit: string;
  ingressHost?: string;
  hpaEnabled: boolean;
  hpaMinReplicas: number;
  hpaMaxReplicas: number;
  hpaCpuTargetPercent: number;
  hpaStatus?: HpaStatusDto;
  revisions: RevisionDto[];
}

export interface ApplicationSpec {
  name: string;
  image: string;
  port?: number;
  replicas?: number;
  cpuRequest?: string;
  cpuLimit?: string;
  memoryRequest?: string;
  memoryLimit?: string;
  envVars?: { key: string; value: string }[];
  ingressHost?: string;
  hpaEnabled?: boolean;
  hpaMinReplicas?: number;
  hpaMaxReplicas?: number;
  hpaCpuTargetPercent?: number;
}

// ── API calls ──────────────────────────────────────────────────────────────

export const api = {
  // Auth check — list projects; throws if key is wrong
  checkAuth: () => request<ProjectDto[]>('/projects'),

  // Projects
  listProjects: () => request<ProjectDto[]>('/projects'),
  getProject: (id: string) => request<ProjectDto>(`/projects/${id}`),
  createProject: (req: CreateProjectRequest) =>
    request<ProjectDto>('/projects', { method: 'POST', body: JSON.stringify(req) }),
  updateProject: (id: string, req: Partial<CreateProjectRequest>) =>
    request<ProjectDto>(`/projects/${id}`, { method: 'PATCH', body: JSON.stringify(req) }),
  deleteProject: (id: string) =>
    request<void>(`/projects/${id}`, { method: 'DELETE' }),

  // API Keys
  listApiKeys: (projectId: string) =>
    request<ApiKeyDto[]>(`/projects/${projectId}/api-keys`),
  createApiKey: (projectId: string, req: CreateApiKeyRequest) =>
    request<ApiKeyDto>(`/projects/${projectId}/api-keys`, {
      method: 'POST', body: JSON.stringify(req),
    }),
  revokeApiKey: (projectId: string, keyId: string) =>
    request<void>(`/projects/${projectId}/api-keys/${keyId}`, { method: 'DELETE' }),

  // Audit
  getAuditLog: (projectId: string, limit = 100) =>
    request<AuditEntryDto[]>(`/projects/${projectId}/audit?limit=${limit}`),

  // Applications
  listApps: () => request<ApplicationSummaryDto[]>('/applications'),
  getApp: (name: string) => request<ApplicationDetailDto>(`/applications/${name}`),
  createApp: (spec: ApplicationSpec) =>
    request<ApplicationDetailDto>('/applications', { method: 'POST', body: JSON.stringify(spec) }),
  updateApp: (name: string, spec: ApplicationSpec) =>
    request<ApplicationDetailDto>(`/applications/${name}`, {
      method: 'PUT', body: JSON.stringify(spec),
    }),
  scaleApp: (name: string, replicas: number) =>
    request<ApplicationSummaryDto>(`/applications/${name}/scale`, {
      method: 'POST', body: JSON.stringify({ replicas }),
    }),
  rollbackApp: (name: string, revision?: number) =>
    request<ApplicationDetailDto>(`/applications/${name}/rollback`, {
      method: 'POST', body: JSON.stringify({ revision: revision ?? null }),
    }),
  deleteApp: (name: string) =>
    request<void>(`/applications/${name}`, { method: 'DELETE' }),

  getRevisions: (name: string) =>
    request<RevisionDto[]>(`/applications/${name}/revisions`),
  getLogs: (name: string, lines = 100) =>
    fetch(`/api/applications/${name}/logs?lines=${lines}`, {
      headers: { 'X-API-Key': getStoredApiKey() ?? '' },
    }).then(r => r.text()),
  getEvents: (name: string) =>
    request<string[]>(`/applications/${name}/events`),
};
