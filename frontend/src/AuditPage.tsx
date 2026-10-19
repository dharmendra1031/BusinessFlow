import { useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import { handleFormKeyDown } from './formUtils';

type AuditEntry = {
  id: number; createdAtUtc: string; tableName: string; recordId: number;
  action: string; changedFields: string | null; userId: number; userName: string | null;
};
type AuditResult = { total: number; page: number; pageSize: number; items: AuditEntry[] };

export default function AuditPage({ token }: { token: string }) {
  const { notify } = useFeedback();
  const [table, setTable] = useState('');
  const [action, setAction] = useState('');
  const [userId, setUserId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [data, setData] = useState<AuditResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);

  async function search(nextPage: number) {
    if (from && to && from > to) {
      notify('warning', 'Invalid date range', 'Start date must not be after end date.');
      return;
    }
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(nextPage), pageSize: '25' });
      if (table.trim()) params.set('table', table.trim());
      if (action.trim()) params.set('action', action.trim());
      if (userId) params.set('userId', userId);
      if (from) params.set('from', from);
      if (to) params.set('to', to);
      setData(await api<AuditResult>('/api/admin/audit?' + params, token));
      setPage(nextPage);
    } catch (error) {
      notify('error', 'Unable to load audit history', error instanceof Error ? error.message : undefined);
    } finally { setLoading(false); }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void search(1);
  }

  return <section>
    <header className="page-header">
      <div><span className="section-kicker">ADMINISTRATION</span><h1>Audit history</h1>
        <p>Review who changed CRM records and when. Admin access only.</p></div>
    </header>
    <section className="panel">
      <form className="table-toolbar" onSubmit={submit} onKeyDown={handleFormKeyDown}>
        <label>Module<input maxLength={60} value={table}
          placeholder="e.g. Leads" onChange={event => setTable(event.target.value)} /></label>
        <label>Action<input maxLength={50} value={action}
          placeholder="e.g. Create" onChange={event => setAction(event.target.value)} /></label>
        <label>User ID<input type="number" min={1} step={1} value={userId}
          onChange={event => setUserId(event.target.value)} /></label>
        <label>From<input type="date" max={to || undefined} value={from}
          onChange={event => setFrom(event.target.value)} /></label>
        <label>To<input type="date" min={from || undefined} value={to}
          onChange={event => setTo(event.target.value)} /></label>
        <button className="primary" disabled={loading}>{loading ? 'Loading…' : 'Search audit'}</button>
      </form>
      {data && <>
        <div className="panel-heading"><h2>{data.total.toLocaleString()} audit entries</h2></div>
        {data.items.length ? <div className="table-scroll"><table>
          <thead><tr><th>Time (UTC)</th><th>User</th><th>Module</th><th>Record</th>
            <th>Action</th><th>Changes</th></tr></thead>
          <tbody>{data.items.map(entry => <tr key={entry.id}>
            <td>{new Date(entry.createdAtUtc).toLocaleString()}</td>
            <td>{entry.userName || 'Former user'} (#{entry.userId})</td>
            <td>{entry.tableName}</td><td>{entry.recordId}</td>
            <td>{entry.action}</td><td>{entry.changedFields || '—'}</td>
          </tr>)}</tbody>
        </table></div> : <p className="field-help">No matching audit entries.</p>}
        <div className="drawer-actions">
          <button type="button" disabled={loading || page === 1} onClick={() => void search(page - 1)}>Previous</button>
          <span>Page {page} of {Math.max(1, Math.ceil(data.total / data.pageSize))}</span>
          <button type="button" disabled={loading || page * data.pageSize >= data.total}
            onClick={() => void search(page + 1)}>Next</button>
        </div>
      </>}
    </section>
  </section>;
}
