import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, apiBlob } from './api';
import { useFeedback } from './Feedback';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import type { Paged } from './types';

type Quote = {
  id: number; leadId: number; leadName: string; title: string; status: string;
  amount: number; currency: string; currentRevision: number; rowVersion: string;
  updatedAtUtc: string;
};
type QuoteLine = {
  id?: number; description: string; quantity: number; unitPrice: number;
  discountRate: number; taxRate: number; totalAmount?: number;
};
type LineForm = {
  description: string; quantity: string; unitPrice: string; discountRate: string; taxRate: string;
};
const blankLine = (): LineForm => ({ description: '', quantity: '1', unitPrice: '', discountRate: '0', taxRate: '0' });
const round = (value: number) => Math.round((value + Number.EPSILON) * 1000) / 1000;
function totals(lines: LineForm[]) {
  return lines.reduce((sum, line) => {
    const base = round(Number(line.quantity) * Number(line.unitPrice));
    const discount = round(base * Number(line.discountRate) / 100);
    const tax = round((base - discount) * Number(line.taxRate) / 100);
    return { subtotal: sum.subtotal + base, discount: sum.discount + discount,
      tax: sum.tax + tax, total: sum.total + base - discount + tax };
  }, { subtotal: 0, discount: 0, tax: 0, total: 0 });
}

type Revision = {
  id: number; revision: number; title: string; scope: string | null;
  amount: number; currency: string; validUntil: string | null;
  paymentTerms: string | null; createdAtUtc: string;
  subtotal: number; discountAmount: number; taxAmount: number; items: QuoteLine[];
};
type Contract = {
  id: number; quotationRevision: number; status: string; terms: string;
  signedBy: string | null; signatureReference: string | null; signedOn: string | null; rowVersion: string;
};
type QuoteDetail = {
  quotation: Pick<Quote, 'id' | 'leadId' | 'leadName' | 'status' | 'currentRevision' | 'rowVersion'> & {
    approvalComment: string | null; approvedAtUtc: string | null;
    clientResponseBy: string | null; clientResponseReference: string | null;
  };
  revisions: Revision[];
  contract: Contract | null;
};
type Lead = { id: number; name: string; companyName: string | null };
type Form = {
  title: string; scope: string; amount: string; currency: string;
  validUntil: string; paymentTerms: string;
};
const blank = (): Form => ({
  title: '', scope: '', amount: '', currency: 'INR', validUntil: '', paymentTerms: '',
});

export default function QuotationsPage({ token, initialLead }: { token: string; initialLead?: Lead | null }) {
  const { notify, confirm } = useFeedback();
  const [items, setItems] = useState<Quote[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<QuoteDetail | null>(null);
  const [creating, setCreating] = useState(Boolean(initialLead));
  const [editingRevision, setEditingRevision] = useState(false);
  const [form, setForm] = useState<Form>(blank);
  const [lines, setLines] = useState<LineForm[]>([blankLine()]);
  const [reviewComment, setReviewComment] = useState('');
  const [responseBy, setResponseBy] = useState('');
  const [responseReference, setResponseReference] = useState('');
  const [contractTerms, setContractTerms] = useState('');
  const [signedBy, setSignedBy] = useState('');
  const [signatureReference, setSignatureReference] = useState('');
  const [signedOn, setSignedOn] = useState('');
  const [leadQuery, setLeadQuery] = useState(initialLead?.name ?? '');
  const [leads, setLeads] = useState<Lead[]>([]);
  const [lead, setLead] = useState<Lead | null>(initialLead ?? null);
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
      setCreating(false);
      setEditingRevision(false);
      setContractTerms(result.revisions[0].paymentTerms ?? '');
      const latest = result.revisions[0];
      setLines(latest.items.map(item => ({
        description: item.description, quantity: String(item.quantity),
        unitPrice: String(item.unitPrice), discountRate: String(item.discountRate),
        taxRate: String(item.taxRate),
      })));
      setForm({
        title: latest.title, scope: latest.scope ?? '', amount: String(latest.amount),
        currency: latest.currency, validUntil: latest.validUntil ?? '',
        paymentTerms: latest.paymentTerms ?? '',
      });
    } catch (err) {
      notify('error', 'Unable to open quotation', err instanceof Error ? err.message : undefined);
    }
  }

  useEffect(() => {
    if (selected || creating)
      document.getElementById('quotation-editor')?.scrollIntoView({ block: 'start' });
  }, [selected?.quotation.id, creating]);

  function update(field: keyof Form, value: string) {
    setForm(previous => ({ ...previous, [field]: value }));
  }

  function updateLine(index: number, field: keyof LineForm, value: string) {
    setLines(rows => rows.map((row, i) => i === index ? { ...row, [field]: value } : row));
  }

  function details() {
    return {
      title: form.title.trim(), scope: form.scope.trim() || null,
      amount: lines.length ? totals(lines).total : Number(form.amount),
      items: lines.length ? lines.map(row => ({
        description: row.description.trim(), quantity: Number(row.quantity),
        unitPrice: Number(row.unitPrice), discountRate: Number(row.discountRate),
        taxRate: Number(row.taxRate),
      })) : null,
      currency: form.currency.toUpperCase().trim(),
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

  async function downloadPdf(id: number, revision: number) {
    try {
      const blob = await apiBlob('/api/admin/quotations/' + id + '/pdf?revision=' + revision, token);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'Quotation-' + id + '-R' + revision + '.pdf';
      link.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 30000);
    } catch (err) {
      notify('error', 'Unable to download quotation PDF', err instanceof Error ? err.message : undefined);
    }
  }

  async function postChange(path: string, body: object, message: string) {
    if (!selected || working) return;
    setWorking(true);
    try {
      await api('/api/admin/quotations/' + selected.quotation.id + path, token, {
        method: 'POST', body: JSON.stringify(body),
      });
      await open(selected.quotation.id);
      await load();
      notify('success', message);
    } catch (err) {
      notify('error', 'Unable to update quotation', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function submitForApproval() {
    if (!selected) return;
    await postChange('/submit', { rowVersion: selected.quotation.rowVersion }, 'Quotation submitted for review');
  }

  async function review(approve: boolean) {
    if (!selected || reviewComment.trim().length < 10) {
      notify('warning', 'Enter a review comment of at least 10 characters.');
      return;
    }
    await postChange('/review', {
      approve, comment: reviewComment.trim(), rowVersion: selected.quotation.rowVersion,
    }, approve ? 'Quotation approved' : 'Quotation returned to draft');
  }

  async function createContract(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected || !validateForm(event.currentTarget) || contractTerms.trim().length < 10) return;
    await postChange('/contract', {
      terms: contractTerms.trim(), rowVersion: selected.quotation.rowVersion,
    }, 'Contract draft created');
  }

  async function signContract(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected?.contract || !validateForm(event.currentTarget)) return;
    if (!(await confirm({
      title: 'Record signed contract?',
      message: 'Make sure the signature evidence is available. This records the signature details, it does not sign the document.',
      confirmText: 'Record signature',
    }))) return;
    await postChange('/contract/sign', {
      signedBy: signedBy.trim(), signatureReference: signatureReference.trim(),
      signedOn, rowVersion: selected.contract.rowVersion,
    }, 'Contract signature recorded');
  }

  async function changeStatus(status: 'Sent' | 'Accepted' | 'Rejected') {
    if (working || !selected) return;
    if (status !== 'Sent' && (responseBy.trim().length < 2 || responseReference.trim().length < 5)) {
      notify('warning', 'Enter the client contact and response reference.');
      return;
    }
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
        method: 'POST', body: JSON.stringify({
          status, rowVersion: selected.quotation.rowVersion,
          clientResponseBy: status === 'Sent' ? null : responseBy.trim(),
          clientResponseReference: status === 'Sent' ? null : responseReference.trim(),
        }),
      });
      await open(id);
      await load();
      notify('success', 'Quotation status updated');
    } catch (err) {
      notify('error', 'Unable to update status', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  const nextStep = selected ? (
    selected.quotation.status === 'Draft' ? 'Submit for internal approval when the quotation is ready.' :
    selected.quotation.status === 'PendingApproval' ? 'Review the quotation and approve it or return it to draft.' :
    selected.quotation.status === 'Approved' ? 'Send the approved quotation to the client.' :
    selected.quotation.status === 'Sent' ? 'Record the client response and its evidence reference.' :
    selected.quotation.status === 'Rejected' ? 'The client declined this quotation. Review the response before proceeding.' :
    selected.contract?.status === 'Signed' ? 'Contract signed. Create an invoice under Finance when billing is due.' :
    selected.contract ? 'Record the signed contract and its supporting reference.' :
    selected.quotation.status === 'Accepted' ? 'Create a contract draft for the accepted quotation.' : ''
  ) : '';

  return <section>
    <header className="page-header"><div>
      <span className="section-kicker">SALES OPERATIONS</span>
      <h1>Sales quotations</h1>
      <p>Select a quotation to review it, or create one from an existing lead.</p>
    </div>
    <button type="button" className="primary" disabled={working}
      onClick={() => {
        setSelected(null); setCreating(true); setForm(blank());
        setLines([blankLine()]); setLead(null); setLeads([]); setLeadQuery('');
      }}>New quotation</button></header>

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

    {(creating || selected) && <section id="quotation-editor" className="panel" aria-label="Quotation editor">
      <div className="panel-heading">
        <h2>{selected ? 'Quotation #' + selected.quotation.id : 'New quotation'}</h2>
        <button type="button" disabled={working} onClick={() => {
          setSelected(null); setCreating(false); setLead(null); setEditingRevision(false);
        }}>Close</button>
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

      {selected && <div className="drawer-actions">
        <button type="button" disabled={working}
          onClick={() => void downloadPdf(selected.quotation.id, selected.quotation.currentRevision)}>
          Download quotation PDF</button>
        {['Draft', 'Approved', 'Sent'].includes(selected.quotation.status) &&
          <button type="button" disabled={working} aria-expanded={editingRevision}
            onClick={() => setEditingRevision(value => !value)}>
            {editingRevision ? 'Close revision form' : 'Create new revision'}
          </button>}
      </div>}
      {selected && <p className="field-help">
        Lead: {selected.quotation.leadName} (#{selected.quotation.leadId}) ·
        Status: {selected.quotation.status} · Revision {selected.quotation.currentRevision}
      </p>}
      {selected && nextStep && <div className="quotation-next-step" aria-label="Quotation next step">
        <strong>Next step</strong><span>{nextStep}</span>
      </div>}
      {selected?.quotation.status === 'Draft' && <div className="drawer-actions">
        <button disabled={working} onClick={() => void submitForApproval()}>Submit for approval</button>
      </div>}
      {selected?.quotation.status === 'PendingApproval' && <section className="project-section">
        <h3>Internal quotation review</h3>
        <label>Review comment *<textarea required minLength={10} maxLength={500}
          value={reviewComment} onChange={event => setReviewComment(event.target.value)} /></label>
        <div className="drawer-actions">
          <button type="button" className="primary" disabled={working || reviewComment.trim().length < 10}
            onClick={() => void review(true)}>Approve quotation</button>
          <button type="button" disabled={working || reviewComment.trim().length < 10}
            onClick={() => void review(false)}>Return to draft</button>
        </div>
      </section>}
      {selected?.quotation.status === 'Approved' && <div className="drawer-actions">
        <button disabled={working} onClick={() => void changeStatus('Sent')}>Mark as sent</button>
      </div>}
      {selected?.quotation.status === 'Sent' && <section className="project-section">
        <h3>Record client response</h3>
        <p className="field-help">Enter who replied and the email or document reference. This is not a digital signature.</p>
        <div className="form-grid">
          <label>Client contact *<input required minLength={2} maxLength={150}
            value={responseBy} onChange={event => setResponseBy(event.target.value)} /></label>
          <label>Response reference *<input required minLength={5} maxLength={500}
            value={responseReference} onChange={event => setResponseReference(event.target.value)} /></label>
        </div>
        <div className="drawer-actions">
          <button disabled={working} onClick={() => void changeStatus('Accepted')}>Record accepted</button>
          <button disabled={working} onClick={() => void changeStatus('Rejected')}>Record rejected</button>
        </div>
      </section>}
      {((creating && !selected) || (editingRevision && selected &&
        ['Draft', 'Approved', 'Sent'].includes(selected.quotation.status))) && <form
        className="form-grid" onSubmit={save} onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
        <label>Quotation title *<input required minLength={2} maxLength={150}
          value={form.title} onChange={event => update('title', event.target.value)} /></label>
        <label>Currency *<input required minLength={3} maxLength={3} pattern="[A-Za-z]{3}"
          value={form.currency} onChange={event => update('currency', event.target.value)} /></label>
        {!lines.length && <label>Amount *<input required type="number" min="0.001"
          max="999999999999999.999" step="0.001" value={form.amount}
          onChange={event => update('amount', event.target.value)} /></label>}
        <label>Valid until<input type="date" value={form.validUntil}
          onChange={event => update('validUntil', event.target.value)} /></label>
        <label>Scope<textarea maxLength={2000} value={form.scope}
          onChange={event => update('scope', event.target.value)} /></label>
        <label>Payment terms<textarea maxLength={1000} value={form.paymentTerms}
          onChange={event => update('paymentTerms', event.target.value)} /></label>
        <div className="form-grid" style={{ gridColumn: '1 / -1' }}>
          <h3>Quotation items</h3>
          {lines.map((row, index) => <div className="project-section" key={index}>
            <div className="form-grid">
              <label>Description *<input required minLength={2} maxLength={250}
                value={row.description} onChange={event => updateLine(index, 'description', event.target.value)} /></label>
              <label>Quantity *<input type="number" required min="0.001" step="0.001"
                value={row.quantity} onChange={event => updateLine(index, 'quantity', event.target.value)} /></label>
              <label>Unit price *<input type="number" required min="0" step="0.001"
                value={row.unitPrice} onChange={event => updateLine(index, 'unitPrice', event.target.value)} /></label>
              <label>Discount %<input type="number" required min="0" max="100" step="0.01"
                value={row.discountRate} onChange={event => updateLine(index, 'discountRate', event.target.value)} /></label>
              <label>Tax %<input type="number" required min="0" max="100" step="0.01"
                value={row.taxRate} onChange={event => updateLine(index, 'taxRate', event.target.value)} /></label>
            </div>
            <button type="button" disabled={working} onClick={() => setLines(rows => rows.filter((_, i) => i !== index))}>
              Remove item</button>
          </div>)}
          {lines.length < 100 && <button type="button" disabled={working}
            onClick={() => setLines(rows => [...rows, blankLine()])}>Add item</button>}
          {!!lines.length && <p className="field-help">
            Subtotal: {totals(lines).subtotal.toFixed(3)} · Discount: {totals(lines).discount.toFixed(3)} ·
            Tax: {totals(lines).tax.toFixed(3)} · Total: {totals(lines).total.toFixed(3)} {form.currency.toUpperCase()}
          </p>}
          {!lines.length && <p className="field-help">
            Legacy amount-only quotation. Add items to enable item-level tax and discount calculation.</p>}
        </div>
        <div><button className="primary" disabled={working || (!selected && !lead)}>
          {selected ? 'Save new revision' : 'Create quotation'}</button></div>
      </form>}
      {selected?.quotation.approvalComment && <p className="field-help">
        Latest internal review: {selected.quotation.approvalComment}</p>}
      {selected?.quotation.clientResponseReference && <p className="field-help">
        Client response: {selected.quotation.clientResponseBy} ·
        {selected.quotation.clientResponseReference}</p>}
      {selected?.quotation.status === 'Accepted' && !selected.contract && <section
        className="project-section" aria-label="Contract handoff">
        <h3>Create contract draft</h3>
        <p className="field-help">Only the accepted quotation revision can be handed off to a contract.</p>
        <form className="form-grid" onSubmit={createContract} onInput={clearFieldError}
          onKeyDown={handleFormKeyDown}>
          <label>Contract terms *<textarea required minLength={10} maxLength={2000}
            value={contractTerms} onChange={event => setContractTerms(event.target.value)} /></label>
          <div><button className="primary" disabled={working}>Create contract draft</button></div>
        </form>
      </section>}
      {selected?.contract && <section className="project-section" aria-label="Contract details">
        <h3>Contract #{selected.contract.id} · {selected.contract.status}</h3>
        <p>Linked to quotation revision {selected.contract.quotationRevision}</p>
        <p>{selected.contract.terms}</p>
        {selected.contract.status === 'Signed' ? <p className="field-help">
          Signed by {selected.contract.signedBy} on {selected.contract.signedOn} ·
          Reference: {selected.contract.signatureReference}</p> :
          <form className="form-grid" onSubmit={signContract} onInput={clearFieldError}
            onKeyDown={handleFormKeyDown}>
            <label>Signed by *<input required minLength={2} maxLength={150}
              value={signedBy} onChange={event => setSignedBy(event.target.value)} /></label>
            <label>Signature evidence reference *<input required minLength={5} maxLength={500}
              value={signatureReference} onChange={event => setSignatureReference(event.target.value)} /></label>
            <label>Signed on *<input type="date" required value={signedOn}
              onChange={event => setSignedOn(event.target.value)} /></label>
            <div><button className="primary" disabled={working}>Record signature</button></div>
          </form>}
      </section>}
      {selected && <details className="project-section" aria-label="Quotation revision history">
        <summary>Revision history ({selected.revisions.length})</summary>
        {selected.revisions.map(revision => <div className="project-task" key={revision.id}>
          <div><strong>Revision {revision.revision} · {revision.title}</strong>
            <button type="button" onClick={() => void downloadPdf(selected.quotation.id, revision.revision)}>
              PDF revision {revision.revision}</button>
            <p>{revision.currency} {revision.amount.toFixed(3)} ·
              {revision.validUntil ? ' Valid until ' + revision.validUntil : ' No expiry'}</p>
            <p>Subtotal {revision.subtotal.toFixed(3)} · Discount {revision.discountAmount.toFixed(3)} ·
              Tax {revision.taxAmount.toFixed(3)}</p>
            {!!revision.items.length && <div className="table-scroll"><table>
              <thead><tr><th>Item</th><th>Quantity</th><th>Rate</th><th>Discount %</th>
                <th>Tax %</th><th>Total</th></tr></thead>
              <tbody>{revision.items.map(line => <tr key={line.id}>
                <td>{line.description}</td><td>{line.quantity}</td><td>{line.unitPrice.toFixed(3)}</td>
                <td>{line.discountRate}</td><td>{line.taxRate}</td>
                <td>{line.totalAmount?.toFixed(3)}</td>
              </tr>)}</tbody></table></div>}
            {revision.scope && <p>{revision.scope}</p>}
            {revision.paymentTerms && <p>Terms: {revision.paymentTerms}</p>}
          </div>
        </div>)}
      </details>}
    </section>}
  </section>;
}
