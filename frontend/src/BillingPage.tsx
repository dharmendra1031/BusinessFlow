import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import type { Paged } from './types';

type Contract = {
  id: number; quotationId: number; rowVersion: string; clientName: string; title: string;
  currency: string; contractAmount: number; billedAmount: number; unbilledAmount: number;
};
type Invoice = {
  id: number; contractId: number; type: string; description: string; billToName: string;
  currency: string; amount: number; paidAmount: number; outstanding: number;
  dueDate: string; status: string;
};
type Payment = { id: number; amount: number; method: string; reference: string; receivedOn: string };
type InvoiceDetails = {
  invoice: Invoice & { rowVersion: string; voidReason: string | null };
  contract: { id: number; rowVersion: string; billedAmount: number };
  payments: Payment[];
};
type Ledger = {
  currency: string; contractAmount: number; unbilledAmount: number;
  collected: number; outstanding: number; invoices: Invoice[]; payments: Payment[];
};

const dateToday = () => new Date().toISOString().slice(0, 10);
const dateDue = () => new Date(Date.now() + 30 * 86400000).toISOString().slice(0, 10);
const money = (amount: number, currency: string) => currency + ' ' + amount.toFixed(3);

export default function BillingPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState('');
  const [page, setPage] = useState(1);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [total, setTotal] = useState(0);
  const [selected, setSelected] = useState<InvoiceDetails | null>(null);
  const [contractSearch, setContractSearch] = useState('');
  const [contracts, setContracts] = useState<Contract[]>([]);
  const [contract, setContract] = useState<Contract | null>(null);
  const [type, setType] = useState('Advance');
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  const [dueDate, setDueDate] = useState(dateDue);
  const [receivedAmount, setReceivedAmount] = useState('');
  const [method, setMethod] = useState('BankTransfer');
  const [reference, setReference] = useState('');
  const [receivedOn, setReceivedOn] = useState(dateToday);
  const [voidReason, setVoidReason] = useState('');
  const [ledger, setLedger] = useState<Ledger | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '20', search: filter });
      const result = await api<Paged<Invoice>>('/api/admin/billing/invoices?' + params, token);
      setInvoices(result.items);
      setTotal(result.total);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Cannot load invoices.');
    } finally { setLoading(false); }
  }, [filter, page, token]);

  useEffect(() => { void load(); }, [load]);

  async function openInvoice(id: number) {
    try {
      setSelected(await api<InvoiceDetails>('/api/admin/billing/invoices/' + id, token));
    } catch (err) {
      notify('error', 'Cannot load invoice', err instanceof Error ? err.message : undefined);
    }
  }

  async function viewLedger(id: number) {
    try {
      setLedger(await api<Ledger>('/api/admin/billing/ledger?contractId=' + id, token));
    } catch (err) {
      notify('error', 'Cannot load ledger', err instanceof Error ? err.message : undefined);
    }
  }

  async function findContracts(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      const params = new URLSearchParams({ search: contractSearch.trim(), pageSize: '20' });
      const result = await api<Paged<Contract>>('/api/admin/billing/contracts?' + params, token);
      setContracts(result.items);
      setContract(null);
    } catch (err) {
      notify('error', 'Cannot search signed contracts', err instanceof Error ? err.message : undefined);
    }
  }

  async function createInvoice(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !contract || !validateForm(event.currentTarget)) return;
    setBusy(true);
    try {
      const result = await api<{ id: number }>('/api/admin/billing/invoices', token, {
        method: 'POST', body: JSON.stringify({
          contractId: contract.id, contractRowVersion: contract.rowVersion,
          type, description: description.trim(), amount: Number(amount), dueDate,
        }),
      });
      setContract(null); setContracts([]); setAmount(''); setDescription('');
      await openInvoice(result.id);
      await load();
      notify('success', 'Invoice created');
    } catch (err) {
      notify('error', 'Cannot create invoice', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  async function receivePayment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !selected || !validateForm(event.currentTarget)) return;
    setBusy(true);
    try {
      const id = selected.invoice.id;
      await api('/api/admin/billing/invoices/' + id + '/payments', token, {
        method: 'POST', body: JSON.stringify({
          amount: Number(receivedAmount), method, reference: reference.trim(),
          receivedOn, invoiceRowVersion: selected.invoice.rowVersion,
        }),
      });
      setReference(''); setReceivedAmount('');
      await openInvoice(id);
      await load();
      if (ledger) await viewLedger(selected.invoice.contractId);
      notify('success', 'Payment received');
    } catch (err) {
      notify('error', 'Cannot record payment', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  async function voidInvoice(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !selected || !validateForm(event.currentTarget)) return;
    const approved = await confirm({
      title: 'Void this invoice?',
      message: 'The invoice cannot be restored. The unbilled contract value will be released.',
      confirmText: 'Void invoice', danger: true,
    });
    if (!approved) return;
    setBusy(true);
    try {
      const id = selected.invoice.id;
      await api('/api/admin/billing/invoices/' + id + '/void', token, {
        method: 'POST', body: JSON.stringify({
          reason: voidReason.trim(), invoiceRowVersion: selected.invoice.rowVersion,
        }),
      });
      setVoidReason('');
      await openInvoice(id);
      await load();
      if (ledger) await viewLedger(selected.invoice.contractId);
      notify('success', 'Invoice voided');
    } catch (err) {
      notify('error', 'Cannot void invoice', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  return <section>
    <header className="page-header"><div>
      <span className="section-kicker">SALES OPERATIONS</span>
      <h1>Invoices &amp; collections</h1>
      <p>Create invoices from signed contracts, receive payments and review outstanding balances.</p>
    </div></header>

    <section className="panel" aria-label="Invoice directory">
      <div className="panel-heading"><h2>Invoices</h2><p>{total} records</p></div>
      <form className="form-grid" onSubmit={event => {
        event.preventDefault(); setPage(1); setFilter(search.trim());
      }}>
        <label>Search client or description<input maxLength={150} value={search}
          onChange={event => setSearch(event.target.value)} /></label>
        <div><button type="submit">Search</button></div>
      </form>
      {error && <p className="error" role="alert">{error}
        <button type="button" onClick={() => void load()}>Retry</button></p>}
      {loading ? <p role="status">Loading invoices…</p> : invoices.length ?
        <div className="table-scroll"><table>
          <thead><tr><th>Invoice</th><th>Client</th><th>Type</th><th>Amount</th>
            <th>Outstanding</th><th>Status</th><th>Action</th></tr></thead>
          <tbody>{invoices.map(item => <tr key={item.id}>
            <td>INV-{item.id}</td><td>{item.billToName}</td><td>{item.type}</td>
            <td>{money(item.amount, item.currency)}</td>
            <td>{money(item.outstanding, item.currency)}</td>
            <td>{item.status}</td>
            <td><button type="button" onClick={() => void openInvoice(item.id)}>View invoice</button></td>
          </tr>)}</tbody>
        </table></div> : <p className="empty-state">No invoices found.</p>}
      <div className="pagination"><span>Page {page} · {total} invoices</span>
        <button disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <button disabled={page * 20 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
      </div>
    </section>

    <section className="panel" aria-label="New invoice">
      <div className="panel-heading"><h2>New invoice</h2><p>Signed contracts only</p></div>
      <form className="form-grid" onSubmit={findContracts}>
        <label>Find signed contract<input value={contractSearch} maxLength={150}
          onChange={event => { setContractSearch(event.target.value); setContract(null); }}
          placeholder="Client name or quotation title" /></label>
        <div><button type="submit" disabled={busy}>Find contracts</button></div>
      </form>
      {contracts.map(item => <div className="project-task" key={item.id}>
        <div><strong>{item.clientName}</strong><p>Contract #{item.id} · {item.title}</p>
          <p>Unbilled: {money(item.unbilledAmount, item.currency)}</p></div>
        <button type="button" disabled={busy || item.unbilledAmount <= 0}
          onClick={() => setContract(item)}>
          {contract?.id === item.id ? 'Selected' : 'Choose contract'}</button>
      </div>)}
      {contract && <form className="form-grid" onSubmit={createInvoice}
        onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
        <p className="field-help">Selected contract #{contract.id} ·
          Unbilled {money(contract.unbilledAmount, contract.currency)}</p>
        <label>Invoice type *<select value={type} onChange={event => setType(event.target.value)}>
          <option value="Advance">Advance</option><option value="Milestone">Milestone</option>
          <option value="Final">Final</option></select></label>
        <label>Description *<input required minLength={3} maxLength={250}
          value={description} onChange={event => setDescription(event.target.value)} /></label>
        <label>Amount ({contract.currency}) *<input type="number" required min="0.001"
          max={contract.unbilledAmount} step="0.001" value={amount}
          onChange={event => setAmount(event.target.value)} /></label>
        <label>Due date *<input type="date" required value={dueDate}
          onChange={event => setDueDate(event.target.value)} /></label>
        <div><button className="primary" disabled={busy}>Create invoice</button></div>
      </form>}
    </section>

    {selected && <section className="panel" aria-label="Invoice details">
      <div className="panel-heading"><h2>Invoice INV-{selected.invoice.id}</h2>
        <p>{selected.invoice.status} · {selected.invoice.billToName}</p></div>
      <p>{selected.invoice.description} · Due {selected.invoice.dueDate}</p>
      <p>Amount: {money(selected.invoice.amount, selected.invoice.currency)} ·
        Received: {money(selected.invoice.paidAmount, selected.invoice.currency)} ·
        Outstanding: <strong>{money(selected.invoice.outstanding, selected.invoice.currency)}</strong></p>
      <button type="button" onClick={() => void viewLedger(selected.invoice.contractId)}>
        View contract ledger</button>

      {selected.invoice.outstanding > 0 && selected.invoice.status !== 'Void' &&
        <form className="form-grid" aria-label="Record payment" onSubmit={receivePayment}
          onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
          <h3>Receive payment</h3>
          <label>Amount *<input type="number" min="0.001" max={selected.invoice.outstanding}
            step="0.001" required value={receivedAmount}
            onChange={event => setReceivedAmount(event.target.value)} /></label>
          <label>Method *<select value={method} onChange={event => setMethod(event.target.value)}>
            <option value="BankTransfer">Bank transfer</option><option value="UPI">UPI</option>
            <option value="Cash">Cash</option><option value="Card">Card</option>
            <option value="Other">Other</option></select></label>
          <label>Unique payment reference *<input required minLength={5} maxLength={100}
            value={reference} onChange={event => setReference(event.target.value)} /></label>
          <label>Received on *<input type="date" required value={receivedOn}
            onChange={event => setReceivedOn(event.target.value)} /></label>
          <div><button className="primary" disabled={busy}>Record payment</button></div>
        </form>}
      {selected.invoice.status === 'Issued' && selected.invoice.paidAmount === 0 &&
        <form className="form-grid" aria-label="Void invoice" onSubmit={voidInvoice}
          onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
          <label>Void reason<input required minLength={10} maxLength={500} value={voidReason}
            onChange={event => setVoidReason(event.target.value)} /></label>
          <div><button type="submit" disabled={busy}>Void invoice</button></div>
        </form>}
      {selected.invoice.voidReason && <p>Void reason: {selected.invoice.voidReason}</p>}
      <h3>Payment history</h3>
      {selected.payments.length ? <div className="table-scroll"><table>
        <thead><tr><th>Received on</th><th>Method</th><th>Reference</th><th>Amount</th></tr></thead>
        <tbody>{selected.payments.map(item => <tr key={item.id}>
          <td>{item.receivedOn}</td><td>{item.method}</td><td>{item.reference}</td>
          <td>{money(item.amount, selected.invoice.currency)}</td>
        </tr>)}</tbody></table></div> : <p className="empty-state">No payments recorded.</p>}
    </section>}

    {ledger && <section className="panel" aria-label="Contract ledger">
      <div className="panel-heading"><h2>Contract ledger</h2></div>
      <p>Contract value: {money(ledger.contractAmount, ledger.currency)} ·
        Unbilled: {money(ledger.unbilledAmount, ledger.currency)}</p>
      <p>Collected: {money(ledger.collected, ledger.currency)} ·
        Invoice outstanding: <strong>{money(ledger.outstanding, ledger.currency)}</strong></p>
      <p className="field-help">Unbilled value is not part of invoice outstanding.</p>
    </section>}
  </section>;
}
