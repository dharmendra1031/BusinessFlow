import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import type { Paged } from './types';

type Quote = {
  id: number; leadId: number; leadName: string; title: string; status: string;
  amount: number; currency: string; currentRevision: number; rowVersion: string;
  updatedAtUtc: string;
};
type Revision = {
  id: number; revision: number; title: string; scope: string | null;
  amount: number; currency: string; validUntil: string | null;
  paymentTerms: string | null; createdAtUtc: string;
};
type QuoteDetail = {
  quotation: Pick<Quote, 'id' | 'leadId' | 'leadName' | 'status' | 'currentRevision' | 'rowVersion'>;
  revisions: Revision[];
};
type Lead = { id: number; name: string; companyName: string | null };
type Form = {
  title: string; scope: string; amount: string; currency: string;
  validUntil: string; paymentTerms: string;
};
const blank = (): Form => ({
  title: '', scope: '', amount: '', currency: 'INR', validUntil: '', paymentTerms: '',
});

export default function QuotationsPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const [items, setItems] = useState<Quote[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<QuoteDetail | null>(null);
  const [form, setForm] = useState<Form>(blank);
  const [leadQuery, setLeadQuery] = useState('');
  const [leads, setLeads] = useState<Lead[]>([]);
  const [lead, setLead] = useState<Lead | null>(null);
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '30', search: query });
      const data = await api<Paged<Quote>>('/api/admin/quotations?' + params, token);
      setItems(data.items);
      setTotal(data.total);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load quotations.');
    } finally { setLoading(false); }
  }, [page, query, token]);

  useEffect(() => { void load(); }, [load]);

  async function open(id: number) {
    try {
      const result = await api<QuoteDetail>('/api/admin/quotations/' + id, token);
      setSelected(result);
      const latest = result.revisions[0];
      setForm({
        title: latest.title, scope: latest.scope ?? '', amount: String(latest.amount),
        currency: latest.currency, validUntil: latest.validUntil ?? '',
        paymentTerms: latest.paymentTerms ?? '',
      });
    } catch (err) {
      notify('error', 'Unable to open quotation', err instanceof Error ? err.message : undefined);
    }
  }

  function update(field: keyof Form, value: string) {
    setForm(previous => ({ ...previous, [field]: value }));
  }

  function details() {
    return {
      title: form.title.trim(), scope: form.scope.trim() || null,
      amount: Number(form.amount), currency: form.currency.toUpperCase().trim(),
      validUntil: form.validUntil || null, paymentTerms: form.paymentTerms.trim() || null,
    };
  }

  async function findLeads(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!leadQuery.trim()) return;
    try {
      const data = await api<Paged<Lead>>('/api/leads?pageSize=15&search=' +
        encodeURIComponent(leadQuery.trim()), token);
      setLeads(data.items);
    } catch (err) {
      notify('error', 'Unable to find leads', err instanceof Error ? err.message : undefined);
    }
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working || !validateForm(event.currentTarget) || (!selected && !lead)) return;
    setWorking(true);
    try {
      if (selected) {
        await api('/api/admin/quotations/' + selected.quotation.id + '/revisions', token, {
          method: 'POST', body: JSON.stringify({ details: details(), rowVersion: selected.quotation.rowVersion }),
        });
        await open(selected.quotation.id);
        notify('success', 'Quotation revision saved');
      } else {
        const created = await api<{ id: number }>('/api/admin/quotations', token, {
          method: 'POST', body: JSON.stringify({ leadId: lead!.id, details: details() }),
        });
        setLead(null); setLeads([]); setLeadQuery('');
        await open(created.id);
        notify('success', 'Quotation created');
      }
      await load();
    } catch (err) {
      notify('error', 'Unable to save quotation', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function changeStatus(status: 'Sent' | 'Accepted' | 'Rejected') {
    if (working || !selected) return;
    const approved = await confirm({
      title: status === 'Sent' ? 'Mark quotation as sent?' : 'Record client response?',
      message: status === 'Sent'
        ? 'Confirm this revision has been sent to the client.'
        : 'Record this quotation as ' + status.toLowerCase() + '. This is an internal record, not client sign-off evidence.',
      confirmText: status === 'Sent' ? 'Mark sent' : 'Record ' + status.toLowerCase(),
      danger: status === 'Rejected',
    });
    if (!approved) return;
    setWorking(true);
    try {
      const id = selected.quotation.id;
      await api('/api/admin/quotations/' + id + '/status', token, {
        method: 'POST', body: JSON.stringify({ status, rowVersion: selected.quotation.rowVersion }),
      });
      await open(id);
      await load();
      notify('success', 'Quotation status updated');
    } catch (err) {
      notify('error', 'Unable to update status', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  return <section>
    <header className="page-header"><div>
      <span className="section-kicker">SALES OPERATIONS</span>
      <h1>Sales quotations</h1>
      <p>One quotation per lead, with a preserved revision history and tracked status.</p>
    </div></header>

    <section className="panel" aria-label="Quotation directory">
      <div className="panel-heading"><h2>Quotations</h2><p>{total} records</p></div>
      <form className="form-grid" onSubmit={event => {
        event.preventDefault(); setPage(1); setQuery(search.trim());
      }}>
        <label>Search quotation or lead<input value={search} maxLength={150}
          onChange={event => setSearch(event.target.value)} /></label>
        <div><button type="submit">Search</button></div>
      </form>
      {error && <p className="error" role="alert">{error}
        <button type="button" onClick={() => void load()}>Retry</button></p>}
      {loading ? <p role="status">Loading quotations…</p> :
        items.length ? <div className="table-scroll"><table>
          <thead><tr><th>Quotation</th><th>Lead</th><th>Amount</th><th>Revision</th>
            <th>Status</th><th>Action</th></tr></thead>
          <tbody>{items.map(item => <tr key={item.id}>
            <td><strong>{item.title}</strong></td>
            <td>{item.leadName}</td><td>{item.currency} {item.amount.toFixed(3)}</td>
            <td>{item.currentRevision}</td><td>{item.status}</td>
            <td><button type="button" onClick={() => void open(item.id)}>View quotation</button></td>
          </tr>)}</tbody>
        </table></div> : <p className="empty-state">No quotations found.</p>}
      <div className="pagination"><span>Page {page} · {total} quotations</span>
        <button disabled={loading || page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <button disabled={loading || page * 30 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
      </div>
    </section>

    <section className="panel" aria-label="Quotation editor">
      <div className="panel-heading">
        <h2>{selected ? 'Quotation #' + selected.quotation.id : 'New quotation'}</h2>
        {selected && <button type="button" disabled={working} onClick={() => {
          setSelected(null); setForm(blank()); setLead(null);
        }}>New quotation</button>}
      </div>

      {!selected && <>
        <form className="form-grid" onSubmit={findLeads}>
          <label>Find an existing lead<input value={leadQuery} maxLength={150}
            onChange={event => { setLeadQuery(event.target.value); setLead(null); setLeads([]); }}
            placeholder="Lead name, company or code" /></label>
          <div><button type="submit" disabled={!leadQuery.trim()}>Find leads</button></div>
        </form>
        {leads.map(item => <div className="project-task" key={item.id}>
          <div><strong>{item.name}</strong><p>{item.companyName || 'Lead'} · #{item.id}</p></div>
          <button type="button" disabled={working} onClick={() => setLead(item)}>
            {lead?.id === item.id ? 'Selected' : 'Choose lead'}</button>
        </div>)}
        {lead && <p className="field-help">Selected lead: {lead.name} (#{lead.id})</p>}
      </>}

      {selected && <p className="field-help">
        Lead: {selected.quotation.leadName} (#{selected.quotation.leadId}) ·
        Status: {selected.quotation.status} · Revision {selected.quotation.currentRevision}
      </p>}
      {selected?.quotation.status === 'Draft' && <div className="drawer-actions">
        <button disabled={working} onClick={() => void changeStatus('Sent')}>Mark as sent</button>
      </div>}
      {selected?.quotation.status === 'Sent' && <div className="drawer-actions">
        <button disabled={working} onClick={() => void changeStatus('Accepted')}>Record accepted</button>
        <button disabled={working} onClick={() => void changeStatus('Rejected')}>Record rejected</button>
      </div>}
      {(!selected || ['Draft', 'Sent'].includes(selected.quotation.status)) && <form
        className="form-grid" onSubmit={save} onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
        <label>Quotation title *<input required minLength={2} maxLength={150}
          value={form.title} onChange={event => update('title', event.target.value)} /></label>
        <label>Currency *<input required minLength={3} maxLength={3} pattern="[A-Za-z]{3}"
          value={form.currency} onChange={event => update('currency', event.target.value)} /></label>
        <label>Amount *<input required type="number" min="0.001" max="999999999999.999" step="0.001"
          value={form.amount} onChange={event => update('amount', event.target.value)} /></label>
        <label>Valid until<input type="date" value={form.validUntil}
          onChange={event => update('validUntil', event.target.value)} /></label>
        <label>Scope<textarea maxLength={2000} value={form.scope}
          onChange={event => update('scope', event.target.value)} /></label>
        <label>Payment terms<textarea maxLength={1000} value={form.paymentTerms}
          onChange={event => update('paymentTerms', event.target.value)} /></label>
        <div><button className="primary" disabled={working || (!selected && !lead)}>
          {selected ? 'Save new revision' : 'Create quotation'}</button></div>
      </form>}
      {selected && <section className="project-section" aria-label="Quotation revision history">
        <h3>Revision history ({selected.revisions.length})</h3>
        {selected.revisions.map(revision => <div className="project-task" key={revision.id}>
          <div><strong>Revision {revision.revision} · {revision.title}</strong>
            <p>{revision.currency} {revision.amount.toFixed(3)} ·
              {revision.validUntil ? ' Valid until ' + revision.validUntil : ' No expiry'}</p>
            {revision.scope && <p>{revision.scope}</p>}
            {revision.paymentTerms && <p>Terms: {revision.paymentTerms}</p>}
          </div>
        </div>)}
      </section>}
    </section>
  </section>;
}
