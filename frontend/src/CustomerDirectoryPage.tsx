import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import type { Paged } from './types';

type Customer = { id: number; name: string; leadCount: number };
type CustomerDetails = {
  customer: { id: number; name: string };
  leadCount: number;
  leads: { id: number; name: string; status: string }[];
};

export default function CustomerDirectoryPage({ token }: { token: string }) {
  const { notify } = useFeedback();
  const [items, setItems] = useState<Customer[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<CustomerDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '30', search: query });
      const result = await api<Paged<Customer>>('/api/customers?' + params, token);
      setItems(result.items);
      setTotal(result.total);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load customers.');
    } finally {
      setLoading(false);
    }
  }, [page, query, token]);

  useEffect(() => { void load(); }, [load]);

  async function openCustomer(id: number) {
    try {
      setSelected(await api<CustomerDetails>('/api/customers/' + id, token));
    } catch (err) {
      notify('error', 'Unable to open customer', err instanceof Error ? err.message : undefined);
    }
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">CLIENT RELATIONSHIPS</span>
      <h1>Customer directory</h1>
      <p>Customers related to leads in your permitted team scope. Read-only access.</p></div></header>
    <section className="panel" aria-label="Scoped customer directory">
      <div className="panel-heading"><h2>Visible customers</h2><p>{total} customer records</p></div>
      <form className="form-grid" onSubmit={event => {
        event.preventDefault(); setPage(1); setQuery(search.trim());
      }}>
        <label>Search customers<input value={search} maxLength={200}
          onChange={event => setSearch(event.target.value)} /></label>
        <div><button type="submit">Search</button></div>
      </form>
      {error && <p role="alert">{error} <button type="button" onClick={() => void load()}>Retry</button></p>}
      {loading ? <p role="status">Loading customers…</p> :
        items.length ? <div className="table-scroll"><table>
          <thead><tr><th>Customer</th><th>Visible leads</th><th>Action</th></tr></thead>
          <tbody>{items.map(customer => <tr key={customer.id}>
            <td><strong>{customer.name}</strong></td><td>{customer.leadCount}</td>
            <td><button type="button" onClick={() => void openCustomer(customer.id)}>View customer</button></td>
          </tr>)}</tbody>
        </table></div> : <p className="empty-state">No customers found in your scope.</p>}
      <div className="pagination"><span>Page {page} · {total} customers</span>
        <button disabled={loading || page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <button disabled={loading || page * 30 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
      </div>
    </section>
    {selected && <section className="panel" aria-label="Read-only customer details">
      <div className="panel-heading"><h2>{selected.customer.name}</h2>
        <button type="button" onClick={() => setSelected(null)}>Close</button></div>
      <h3>Visible leads ({selected.leadCount})</h3>
      {selected.leads.map(lead => <div key={lead.id} className="project-task">
        <div><strong>{lead.name}</strong><p>{lead.status}</p></div>
      </div>)}
      {selected.leadCount > selected.leads.length &&
        <p className="field-help">Showing latest 100 permitted leads.</p>}
    </section>}
  </section>;
}
