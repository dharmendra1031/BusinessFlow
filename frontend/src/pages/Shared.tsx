import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../api';
import { Icon } from '../Icon';

export function useResource<T>(path: string, token: string) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const latestRequest = useRef(0);
  const refresh = useCallback(async () => {
    const requestId = ++latestRequest.current;
    if (!path) { setLoading(false); return; }
    setLoading(true);
    try {
      const result = await api<T>(path, token);
      if (requestId !== latestRequest.current) return;
      setData(result);
      setError('');
    } catch (err) {
      if (requestId !== latestRequest.current) return;
      setError(err instanceof Error ? err.message : 'Unable to load data');
    } finally { if (requestId === latestRequest.current) setLoading(false); }
  }, [path, token]);
  useEffect(() => { void refresh(); }, [refresh]);
  return { data, error, loading, refresh };
}

export function EmptyState({ title, detail }: { title: string; detail: string }) {
  return <div className="empty-state">
    <span className="empty-icon"><Icon name="clipboard" size={27} /></span>
    <h3>{title}</h3><p>{detail}</p>
  </div>;
}

export function ErrorState({ message, retry }: { message: string; retry: () => void }) {
  return <div className="error-state" role="alert">
    <Icon name="warning" size={19} /><span>{message}</span>
    <button onClick={retry}>Retry</button>
  </div>;
}

export function StatusBadge({ status }: { status: string }) {
  const level = status.toLowerCase().replace(/[^a-z]/g, '');
  return <span className={'status-badge status-' + level}><span className="status-dot" />{status}</span>;
}

export function dateLabel(date: string) {
  return new Date(date).toLocaleString(undefined, {
    day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit'
  });
}
