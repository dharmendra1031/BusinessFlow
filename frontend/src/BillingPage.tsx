import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, apiBlob } from './api';
import { useFeedback } from './Feedback';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import type { Paged } from './types';
import CustomerLedgerSection from './CustomerLedgerSection';
import FinancialApprovalsSection from './FinancialApprovalsSection';

type Contract = {
  id: number; quotationId: number; rowVersion: string; clientName: string; title: string;
  currency: string; contractAmount: number; billedAmount: number; unbilledAmount: number;
};
type Invoice = {
  id: number; contractId: number; type: string; description: string; billToName: string;
  currency: string; amount: number; paidAmount: number; outstanding: number;
  dueDate: string; status: string; creditedAmount: number; refundedAmount: number;
  netAmount: number; refundDue: number;
};
type Payment = {
  id: number; amount: number; refundedAmount: number; method: string; reference: string; receivedOn: string;
};
type CreditNote = { id: number; amount: number; reason: string; issuedOn: string };
type Refund = {
  id: number; paymentId: number; amount: number; method: string; reference: string;
  reason: string; refundedOn: string;
};
type InvoiceDetails = {
  invoice: Invoice & { rowVersion: string; voidReason: string | null };
  contract: { id: number; rowVersion: string; billedAmount: number };
  payments: Payment[];
  credits: CreditNote[];
  refunds: Refund[];
};
type Ledger = {
  currency: string; contractAmount: number; unbilledAmount: number;
  collected: number; outstanding: number; credited: number; refunded: number;
  refundDue: number; invoices: Invoice[]; payments: Payment[];
};

type OutstandingRow = {
  id: number; billToName: string; currency: string; description: string;
  amount: number; paidAmount: number; outstanding: number; dueDate: string; status: string;
};
type OutstandingReport = {
  asOf: string; total: number; page: number;
  summary: { currency: string; invoiceCount: number; invoiced: number;
    collected: number; outstanding: number }[];
  items: OutstandingRow[];
};
type CollectionRow = {
  id: number; invoiceId: number; billToName: string; currency: string;
  amount: number; method: string; reference: string; receivedOn: string;
};
type CollectionReport = {
  from: string; to: string; total: number; page: number;
  summary: {
    currency: string; paymentCount: number; refundCount: number;
    collected: number; refunded: number; netCollected: number;
  }[];
  items: CollectionRow[];
};
type ReportMode = 'outstanding' | 'collections';

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
  const [showCreate, setShowCreate] = useState(false);
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
  const [creditAmount, setCreditAmount] = useState('');
  const [creditReason, setCreditReason] = useState('');
  const [creditDate, setCreditDate] = useState(dateToday);
  const [refundPaymentId, setRefundPaymentId] = useState('');
  const [refundAmount, setRefundAmount] = useState('');
  const [refundMethod, setRefundMethod] = useState('BankTransfer');
  const [refundReference, setRefundReference] = useState('');
  const [refundReason, setRefundReason] = useState('');
  const [refundDate, setRefundDate] = useState(dateToday);
  const [approvalsRefresh, setApprovalsRefresh] = useState(0);
  const [ledger, setLedger] = useState<Ledger | null>(null);
  const [reportMode, setReportMode] = useState<ReportMode>('outstanding');
  const [reportSearch, setReportSearch] = useState('');
  const [reportCurrency, setReportCurrency] = useState('');
  const [overdueOnly, setOverdueOnly] = useState(false);
  const [collectionFrom, setCollectionFrom] = useState(() => dateToday().slice(0, 7) + '-01');
  const [collectionTo, setCollectionTo] = useState(dateToday);
  const [reportPage, setReportPage] = useState(1);
  const [outstanding, setOutstanding] = useState<OutstandingReport | null>(null);
  const [collections, setCollections] = useState<CollectionReport | null>(null);
  const [reportError, setReportError] = useState('');
  const [reportLoading, setReportLoading] = useState(false);
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

  async function downloadInvoice(id: number) {
    try {
      const blob = await apiBlob('/api/admin/billing/invoices/' + id + '/pdf', token);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'Invoice-' + id + '.pdf';
      link.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 30000);
    } catch (err) {
      notify('error', 'Cannot download invoice', err instanceof Error ? err.message : undefined);
    }
  }

  async function loadReport(mode: ReportMode, targetPage = 1) {
    setReportLoading(true);
    setReportError('');
    try {
      const params = new URLSearchParams({
        search: reportSearch.trim(), currency: reportCurrency.trim().toUpperCase(),
        page: String(targetPage), pageSize: '25',
      });
      if (mode === 'outstanding') {
        if (overdueOnly) params.set('overdueOnly', 'true');
        const result = await api<OutstandingReport>(
          '/api/admin/billing/reports/outstanding?' + params, token);
        setOutstanding(result);
        setCollections(null);
      } else {
        params.set('from', collectionFrom);
        params.set('to', collectionTo);
        const result = await api<CollectionReport>(
          '/api/admin/billing/reports/collections?' + params, token);
        setCollections(result);
        setOutstanding(null);
      }
      setReportPage(targetPage);
    } catch (err) {
      setReportError(err instanceof Error ? err.message : 'Cannot load financial report.');
    } finally { setReportLoading(false); }
  }

  async function runReport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await loadReport(reportMode);
  }

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
      setShowCreate(false);
      setOutstanding(null); setCollections(null);
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
      setOutstanding(null); setCollections(null);
      await openInvoice(id);
      await load();
      if (ledger) await viewLedger(selected.invoice.contractId);
      notify('success', 'Payment received');
    } catch (err) {
      notify('error', 'Cannot record payment', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  async function issueCreditNote(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !selected || !validateForm(event.currentTarget)) return;
    const approved = await confirm({
      title: 'Submit credit request?',
      message: 'Another Admin must review this credit. Financial balances stay unchanged until approval.',
      confirmText: 'Submit request',
    });
    if (!approved) return;
    setBusy(true);
    try {
      const id = selected.invoice.id;
      await api('/api/admin/billing/invoices/' + id + '/credits', token, {
        method: 'POST', body: JSON.stringify({
          amount: Number(creditAmount), reason: creditReason.trim(),
          issuedOn: creditDate, invoiceRowVersion: selected.invoice.rowVersion,
        }),
      });
      setCreditAmount(''); setCreditReason('');
      await openInvoice(id); await load();
      setOutstanding(null); setCollections(null);
      if (ledger) await viewLedger(selected.invoice.contractId);
      setApprovalsRefresh(value => value + 1);
      notify('success', 'Credit request submitted for independent approval');
    } catch (err) {
      notify('error', 'Cannot issue credit note', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  async function recordRefund(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !selected || !validateForm(event.currentTarget)) return;
    const approved = await confirm({
      title: 'Submit refund for approval?',
      message: 'A second Admin must verify this completed external refund before it is posted.',
      confirmText: 'Submit request',
    });
    if (!approved) return;
    setBusy(true);
    try {
      const id = selected.invoice.id;
      await api('/api/admin/billing/invoices/' + id + '/refunds', token, {
        method: 'POST', body: JSON.stringify({
          paymentId: Number(refundPaymentId), amount: Number(refundAmount),
          method: refundMethod, reference: refundReference.trim(),
          reason: refundReason.trim(), refundedOn: refundDate,
          invoiceRowVersion: selected.invoice.rowVersion,
        }),
      });
      setRefundPaymentId(''); setRefundAmount(''); setRefundReference(''); setRefundReason('');
      await openInvoice(id); await load();
      setOutstanding(null); setCollections(null);
      if (ledger) await viewLedger(selected.invoice.contractId);
      setApprovalsRefresh(value => value + 1);
      notify('success', 'Refund request submitted for independent approval');
    } catch (err) {
      notify('error', 'Cannot record refund', err instanceof Error ? err.message : undefined);
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
      setOutstanding(null); setCollections(null);
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
      <p>Open an invoice to record payments or review its balance.</p>
    </div>
    <button type="button" className="primary" disabled={busy}
      aria-expanded={showCreate} aria-controls="invoice-create"
      onClick={() => setShowCreate(value => !value)}>
      {showCreate ? 'Close form' : 'Create invoice'}
    </button></header>

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

    {showCreate && <section id="invoice-create" className="panel" aria-label="New invoice">
      <div className="panel-heading"><h2>New invoice</h2><p>Signed contracts only</p></div>
      <form className="form-grid" onSubmit={findContracts}>
        <label>Find signed contract<input autoFocus value={contractSearch} maxLength={150}
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
    </section>}

    {selected && <section className="panel" aria-label="Invoice details">
      <div className="panel-heading"><h2>Invoice INV-{selected.invoice.id}</h2>
        <p>{selected.invoice.status} · {selected.invoice.billToName}</p></div>
      <p>{selected.invoice.description} · Due {selected.invoice.dueDate}</p>
      <div className="invoice-summary" aria-label="Invoice balance summary">
        <div><span>Amount:</span> <strong>{money(selected.invoice.amount, selected.invoice.currency)}</strong></div>
        <div><span>Net collected:</span> <strong>{money(selected.invoice.paidAmount, selected.invoice.currency)}</strong></div>
        <div><span>Credited:</span> <strong>{money(selected.invoice.creditedAmount, selected.invoice.currency)}</strong></div>
        <div><span>Outstanding:</span> <strong>{money(selected.invoice.outstanding, selected.invoice.currency)}</strong></div>
        <div><span>Refund due:</span> <strong>{money(selected.invoice.refundDue, selected.invoice.currency)}</strong></div>
      </div>
      <div className="drawer-actions">
        <button type="button" onClick={() => void viewLedger(selected.invoice.contractId)}>
          View contract ledger</button>
        <button type="button" onClick={() => void downloadInvoice(selected.invoice.id)}>
          Download invoice PDF</button>
      </div>
      <p className="field-help">Commercial billing document. Tax registration and tax breakup are not configured.</p>

      {selected.invoice.outstanding > 0 && selected.invoice.status !== 'Void' &&
        <details className="project-section">
          <summary>Record payment</summary>
        <form className="form-grid" aria-label="Record payment" onSubmit={receivePayment}
          onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
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
        </form></details>}
      {selected.invoice.status === 'Issued' && selected.invoice.paidAmount === 0 &&
        selected.invoice.creditedAmount === 0 &&
        <details className="project-section">
          <summary>Void invoice</summary>
        <form className="form-grid" aria-label="Void invoice" onSubmit={voidInvoice}
          onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
          <label>Void reason<input required minLength={10} maxLength={500} value={voidReason}
            onChange={event => setVoidReason(event.target.value)} /></label>
          <div><button type="submit" disabled={busy}>Void invoice</button></div>
        </form></details>}
      {selected.invoice.voidReason && <p>Void reason: {selected.invoice.voidReason}</p>}
      {selected.invoice.status !== 'Void' && selected.invoice.creditedAmount < selected.invoice.amount &&
        <details className="project-section">
          <summary>Request credit note</summary>
        <form className="form-grid" aria-label="Issue credit note" onSubmit={issueCreditNote}
          onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
          <p className="field-help">Reducing the invoice may create a refund due if the customer has paid more than the revised amount.</p>
          <label>Credit amount *<input required type="number" min="0.001" step="0.001"
            max={selected.invoice.amount - selected.invoice.creditedAmount} value={creditAmount}
            onChange={event => setCreditAmount(event.target.value)} /></label>
          <label>Reason *<input required minLength={10} maxLength={500} value={creditReason}
            onChange={event => setCreditReason(event.target.value)} /></label>
          <label>Issue date *<input type="date" required value={creditDate}
            onChange={event => setCreditDate(event.target.value)} /></label>
          <div><button type="submit" disabled={busy}>Submit credit request</button></div>
        </form></details>}
      {!!selected.credits.length && <section aria-label="Credit note history">
        <h3>Credit note history</h3>
        {selected.credits.map(credit => <p key={credit.id}>
          CN-{credit.id} · {credit.issuedOn} ·
          {money(credit.amount, selected.invoice.currency)} · {credit.reason}
        </p>)}
      </section>}
      {selected.invoice.refundDue > 0 && <details className="project-section">
        <summary>Request refund: {money(selected.invoice.refundDue, selected.invoice.currency)} due</summary>
        <form className="form-grid" aria-label="Record refund" onSubmit={recordRefund}
        onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
        <label>Original payment *<select required value={refundPaymentId}
          onChange={event => setRefundPaymentId(event.target.value)}>
          <option value="">Choose payment</option>
          {selected.payments.filter(p => p.amount > p.refundedAmount).map(payment =>
            <option key={payment.id} value={payment.id}>
              #{payment.id} · {payment.reference} · Available:
              {money(payment.amount - payment.refundedAmount, selected.invoice.currency)}
            </option>)}
        </select></label>
        <label>Refund amount *<input required type="number" min="0.001" step="0.001"
          max={selected.invoice.refundDue} value={refundAmount}
          onChange={event => setRefundAmount(event.target.value)} /></label>
        <label>Method *<select value={refundMethod} onChange={event => setRefundMethod(event.target.value)}>
          <option value="BankTransfer">Bank transfer</option><option value="UPI">UPI</option>
          <option value="Cash">Cash</option><option value="Card">Card</option>
          <option value="Other">Other</option></select></label>
        <label>Unique refund reference *<input required minLength={5} maxLength={100}
          value={refundReference} onChange={event => setRefundReference(event.target.value)} /></label>
        <label>Reason *<input required minLength={10} maxLength={500} value={refundReason}
          onChange={event => setRefundReason(event.target.value)} /></label>
        <label>Refund date *<input required type="date" value={refundDate}
          onChange={event => setRefundDate(event.target.value)} /></label>
        <div><button type="submit" disabled={busy}>Submit refund request</button></div>
      </form></details>}
      {!!selected.refunds.length && <section aria-label="Refund history">
        <h3>Refund history</h3>
        {selected.refunds.map(refund => <p key={refund.id}>
          RF-{refund.id} · {refund.refundedOn} ·
          {money(refund.amount, selected.invoice.currency)} ·
          {refund.method} · {refund.reference} · {refund.reason}
        </p>)}
      </section>}
      <h3>Payment history</h3>
      {selected.payments.length ? <div className="table-scroll"><table>
        <thead><tr><th>Received on</th><th>Method</th><th>Reference</th><th>Amount</th></tr></thead>
        <tbody>{selected.payments.map(item => <tr key={item.id}>
          <td>{item.receivedOn}</td><td>{item.method}</td><td>{item.reference}</td>
          <td>{money(item.amount, selected.invoice.currency)}{item.refundedAmount > 0 &&
            <> · Refunded: {money(item.refundedAmount, selected.invoice.currency)}</>}</td>
        </tr>)}</tbody></table></div> : <p className="empty-state">No payments recorded.</p>}
    </section>}

    <details className="panel billing-reports" aria-label="Billing reports">
      <summary>Outstanding &amp; collection reports</summary>
      <p className="field-help">Currency-wise totals are never mixed.</p>
      <form className="form-grid" onSubmit={runReport}>
        <label>Report type
          <select value={reportMode} onChange={event => {
            setReportMode(event.target.value as ReportMode); setReportPage(1);
            setOutstanding(null); setCollections(null);
          }}>
            <option value="outstanding">Outstanding invoices</option>
            <option value="collections">Payments collected</option>
          </select>
        </label>
        <label>Currency (optional)
          <input value={reportCurrency} maxLength={3} pattern="[A-Za-z]{3}"
            placeholder="INR, KWD…" onChange={event => setReportCurrency(event.target.value)} />
        </label>
        <label>Search client or reference
          <input value={reportSearch} maxLength={150}
            onChange={event => setReportSearch(event.target.value)} />
        </label>
        {reportMode === 'outstanding' ?
          <label><input type="checkbox" checked={overdueOnly}
            onChange={event => setOverdueOnly(event.target.checked)} /> Overdue only</label> :
          <>
            <label>From date *<input type="date" required value={collectionFrom}
              onChange={event => setCollectionFrom(event.target.value)} /></label>
            <label>To date *<input type="date" required value={collectionTo}
              onChange={event => setCollectionTo(event.target.value)} /></label>
          </>}
        <div><button type="submit" disabled={reportLoading}>
          {reportLoading ? 'Loading…' : 'Run report'}</button></div>
      </form>
      {reportError && <p className="error" role="alert">{reportError}</p>}

      {outstanding && <>
        <h3>Outstanding invoices ({outstanding.total})</h3>
        {outstanding.summary.map(item => <p className="field-help" key={item.currency}>
          {item.currency}: {item.invoiceCount} invoices ·
          Invoiced {money(item.invoiced, item.currency)} ·
          Received {money(item.collected, item.currency)} ·
          Outstanding <strong>{money(item.outstanding, item.currency)}</strong></p>)}
        {outstanding.items.length ? <div className="table-scroll"><table>
          <thead><tr><th>Invoice</th><th>Client</th><th>Due</th>
            <th>Amount</th><th>Paid</th><th>Outstanding</th></tr></thead>
          <tbody>{outstanding.items.map(item => <tr key={item.id}>
            <td>INV-{item.id}</td><td>{item.billToName}</td><td>{item.dueDate}</td>
            <td>{money(item.amount, item.currency)}</td>
            <td>{money(item.paidAmount, item.currency)}</td>
            <td>{money(item.outstanding, item.currency)}</td>
          </tr>)}</tbody>
        </table></div> : <p className="empty-state">No outstanding invoices match these filters.</p>}
      </>}

      {collections && <>
        <h3>Collections ({collections.total})</h3>
        {collections.summary.map(item => <p className="field-help" key={item.currency}>
          {item.currency}: {item.paymentCount} receipts · {item.refundCount} refunds ·
          Gross receipts {money(item.collected, item.currency)} ·
          Refunded {money(item.refunded, item.currency)} ·
          Net cash collected <strong>{money(item.netCollected, item.currency)}</strong></p>)}
        {collections.items.length ? <div className="table-scroll"><table>
          <thead><tr><th>Date</th><th>Invoice</th><th>Client</th>
            <th>Method</th><th>Reference</th><th>Amount</th></tr></thead>
          <tbody>{collections.items.map(item => <tr key={item.id}>
            <td>{item.receivedOn}</td><td>INV-{item.invoiceId}</td>
            <td>{item.billToName}</td><td>{item.method}</td><td>{item.reference}</td>
            <td>{money(item.amount, item.currency)}</td>
          </tr>)}</tbody>
        </table></div> : <p className="empty-state">No payments match these filters.</p>}
      </>}

      {(outstanding || collections) && <div className="pagination">
        <span>Page {reportPage} · {(outstanding ?? collections)?.total ?? 0} records</span>
        <button type="button" disabled={reportLoading || reportPage <= 1}
          onClick={() => void loadReport(reportMode, reportPage - 1)}>Previous</button>
        <button type="button" disabled={reportLoading ||
          reportPage * 25 >= ((outstanding ?? collections)?.total ?? 0)}
          onClick={() => void loadReport(reportMode, reportPage + 1)}>Next</button>
      </div>}
    </details>

    <FinancialApprovalsSection token={token} refreshKey={approvalsRefresh}
      onDecision={id => {
        void openInvoice(id);
        void load();
        setOutstanding(null); setCollections(null);
        if (ledger && selected) void viewLedger(selected.invoice.contractId);
      }} />

    <CustomerLedgerSection token={token} openInvoice={id => void openInvoice(id)} />

    {ledger && <section className="panel" aria-label="Contract ledger">
      <div className="panel-heading"><h2>Contract ledger</h2></div>
      <p>Contract value: {money(ledger.contractAmount, ledger.currency)} ·
        Unbilled: {money(ledger.unbilledAmount, ledger.currency)}</p>
      <p>Net collected: {money(ledger.collected, ledger.currency)} ·
        Credited: {money(ledger.credited, ledger.currency)} ·
        Refunded: {money(ledger.refunded, ledger.currency)} ·
        Invoice outstanding: <strong>{money(ledger.outstanding, ledger.currency)}</strong> ·
        Refund due: <strong>{money(ledger.refundDue, ledger.currency)}</strong></p>
      <p className="field-help">Unbilled value is not part of invoice outstanding.</p>
    </section>}
  </section>;
}
