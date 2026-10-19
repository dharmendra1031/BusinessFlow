import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { Icon } from './Icon';

type AuditRow = {
  id: number; createdAtUtc: string; userId: number; userName: string;
  action: string; tableName: string; recordId: number; changedFields: string | null;
};
type AuditResult = { total: number; page: number; pageSize: number; items: AuditRow[] };

export default function AuditLogsPage({ token }: { token: string }) {
  const [data, setData] = useState<AuditResult | null>(null);
  const [page, setPage] = useState(1);
  const [table, setTable] = useState('');
  const [action, setAction] = useState('');
  const [userId, setUserId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [filters, setFilters] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const query = new URLSearchParams(filters);
      query.set('page', String(page));
      query.set('pageSize', '25');
      const result = await api<AuditResult>('/api/admin/audit-logs?' + query, token);
      setData(result);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load audit logs.');
    } finally { setLoading(false); }
  }, [filters, page, token]);

  useEffect(() => { void load(); }, [load]);

  function search(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (from && to && from > to) {
      setError('End date cannot be before start date.');
      return;
    }
    const query = new URLSearchParams();
    if (table.trim()) query.set('table', table.trim());
    if (action.trim()) query.set('action', action.trim());
    if (userId.trim()) query.set('userId', userId.trim());
    if (from) query.set('from', from);
    if (to) query.set('to', to);
    setPage(1);
    setFilters(query.toString());
  }

  return <section>
    <header className="page-header">
      <div><span className="section-kicker">ADMINISTRATION</span>
        <h1>Audit logs</h1>
        <p>Review recorded changes and actions across your CRM.</p>
      </div>
      <button className="secondary-action" onClick={() => void load()}>
        <Icon name="refresh" size={17} /> Refresh
      </button>
    </header>
    <section className="panel">
      <div className="panel-heading"><div><h2>Activity history</h2>
        <p>Admin-only, latest activity first</p></div></div>
      <form className="table-toolbar" onSubmit={search}>
        <label>Module<input aria-label="Audit module" maxLength={60} placeholder="e.g. Leads"
          value={table} onChange={event => setTable(event.target.value)} /></label>
        <label>Action<input aria-label="Audit action" maxLength={50} placeholder="e.g. Update"
          value={action} onChange={event => setAction(event.target.value)} /></label>
        <label>User ID<input aria-label="Audit user ID" type="number" min={1} step={1}
          value={userId} onChange={event => setUserId(event.target.value)} /></label>
        <label>From<input aria-label="Audit from date" type="date" max={to || undefined}
          value={from} onChange={event => setFrom(event.target.value)} /></label>
        <label>To<input aria-label="Audit to date" type="date" min={from || undefined}
          value={to} onChange={event => setTo(event.target.value)} /></label>
        <button type="submit" className="primary">Apply filters</button>
      </form>
      {error && <div className="error-state" role="alert">{error}</div>}
      {loading && <p role="status" className="field-help">Loading activity...</p>}
      {data && <p className="field-help">{data.total} matching entries</p>}
      {data?.items.length ? <div className="table-scroll"><table>
        <thead><tr><th>When (UTC)</th><th>User</th><th>Module</th>
          <th>Action</th><th>Record ID</th><th>Details</th></tr></thead>
        <tbody>{data.items.map(row => <tr key={row.id}>
          <td>{new Date(row.createdAtUtc).toLocaleString()}</td>
          <td>{row.userName} (#{row.userId})</td><td>{row.tableName}</td>
          <td>{row.action}</td><td>{row.recordId}</td><td>{row.changedFields || '—'}</td>
        </tr>)}</tbody>
      </table></div> : !loading && <div className="empty-state">
        <h3>No audit entries found</h3><p>Change your filters or try again.</p></div>}
      {data && data.total > data.pageSize && <div className="drawer-actions">
        <button type="button" disabled={page <= 1 || loading} onClick={() => setPage(value => value - 1)}>
          Previous
        </button>
        <span>Page {page} of {Math.ceil(data.total / data.pageSize)}</span>
        <button type="button" disabled={page * data.pageSize >= data.total || loading}
          onClick={() => setPage(value => value + 1)}>Next</button>
      </div>}
    </section>
  </section>;
}
