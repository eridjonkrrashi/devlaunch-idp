interface Props {
  status: string;
  phase?: string;
}

const statusColors: Record<string, string> = {
  Running: 'bg-green-100 text-green-800',
  Failed: 'bg-red-100 text-red-800',
  Degraded: 'bg-yellow-100 text-yellow-800',
  Pending: 'bg-blue-100 text-blue-800',
  Stopped: 'bg-gray-100 text-gray-700',
};

const phaseColors: Record<string, string> = {
  Complete: 'bg-green-100 text-green-800',
  InProgress: 'bg-blue-100 text-blue-800',
  Failed: 'bg-red-100 text-red-800',
  RolledBack: 'bg-orange-100 text-orange-800',
};

export function StatusBadge({ status, phase }: Props) {
  const statusCls = statusColors[status] ?? 'bg-gray-100 text-gray-700';
  const phaseCls = phase ? (phaseColors[phase] ?? 'bg-gray-100 text-gray-700') : '';

  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${statusCls}`}>
        {status}
      </span>
      {phase && phase !== 'Complete' && (
        <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${phaseCls}`}>
          {phase}
        </span>
      )}
    </span>
  );
}
