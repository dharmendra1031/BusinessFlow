import { useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import type { Paged } from './types';

type Customer = { id: number; name: string; legalName: string | null };
type LedgerRow = {
  id: number; contractId: number; description: string; currency: string;
  amount: number; paidAmount: number; creditedAmount: number; refundedAmount: number;
  outstanding: number; refundDue: number; status: string; dueDate: string;
};
type Ledger = {
  customer: Customer; total: number; page: number;
  summary: {
    currency: string; invoiceCount: number; invoiced: number; collected: number;
    credited: number; refunded: number; outstanding: number; refundDue: number;
  }[];
  items: LedgerRow[];
};
const money = (amount: number, currency: string) => currency + ' ' + amount.toFixed(3);

export default function CustomerLedgerSection({ token, openInvoice }: {
  token: string; openInvoice: (id: number) => void;
}) {
  const { notify } = useFeedback();
  const [search, setSearch] = useState('');
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [selected, setSelected] = useState<Customer | null>(null);
  const [currency, setCurrency] = useState('');
  const [ledger, setLedger] = useState<Ledger | null>(null);
  const [loading, setLoading] = useState(false);

  async function searchCustomers(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setLoading(true);
    try {
      const params = new URLSearchParams({ search: search.trim(), pageSize: '20' });
      const result = await api<Paged<Customer>>('/api/admin/billing/customers?' + params, token);
      setCustomers(result.items);
      setSelected(null); setLedger(null);
    } catch (err) {
      notify('error', 'Cannot search customers', err instanceof Error ? err.message : undefined);
    } finally { setLoading(false); }
  }

  async function loadLedger(customer: Customer, page = 1) {
    setLoading(true);
    try {
      const params = new URLSearchParams({ currency: currency.trim().toUpperCase(), page: String(page), pageSize: '25' });
      const result = await api<Ledger>(
        '/api/admin/billing/customers/' + customer.id + '/ledger?' + params, token);
      setSelected(customer); setLedger(result);
    } catch (err) {
      notify('error', 'Cannot load customer ledger', err instanceof Error ? err.message : undefined);
    } finally { setLoading(false); }
  }

  return <section className="panel" aria-label="Customer ledger">
    <div className="panel-heading">
      <h2>Customer-wise ledger</h2>
      <p>Invoices linked through Customer Master · currency-wise balances</p>
    </div>
    <form className="form-grid" onSubmit={searchCustomers}>
      <label>Find customer<input maxLength={150} value={search}
        onChange={event => setSearch(event.target.value)} placeholder="Customer name" /></label>
      <div><button type="submit" disabled={loading}>Find customers</button></div>
    </form>
    {customers.map(customer => <div className="project-task" key={customer.id}>
      <div><strong>{customer.name}</strong>
        {customer.legalName && <p>{customer.legalName}</p>}</div>
      <button type="button" disabled={loading} onClick={() => void loadLedger(customer)}>
        View ledger</button>
    </div>)}
    {selected && <form className="form-grid" onSubmit={event => {
      event.preventDefault(); void loadLedger(selected);
    }}>
      <label>Currency filter<input maxLength={3} pattern="[A-Za-z]{3}"
        placeholder="All currencies" value={currency}
        onChange={event => setCurrency(event.target.value)} /></label>
      <div><button type="submit" disabled={loading}>Apply filter</button></div>
    </form>}
    {ledger && <>
      <h3>{ledger.customer.name} · {ledger.total} invoices</h3>
      {ledger.summary.map(item => <p className="field-help" key={item.currency}>
        <strong>{item.currency}</strong> · Net invoiced {money(item.invoiced, item.currency)} ·
        Collected {money(item.collected, item.currency)} ·
        Credited {money(item.credited, item.currency)} ·
        Refunded {money(item.refunded, item.currency)} ·
        Outstanding {money(item.outstanding, item.currency)} ·
        Refund due {money(item.refundDue, item.currency)}
      </p>)}
      {ledger.items.length ? <div className="table-scroll"><table>
        <thead><tr><th>Invoice</th><th>Description</th><th>Gross</th>
          <th>Credit</th><th>Net collected</th><th>Outstanding</th>
          <th>Refund due</th><th>Status</th><th></th></tr></thead>
        <tbody>{ledger.items.map(item => <tr key={item.id}>
          <td>INV-{item.id}</td><td>{item.description}</td>
          <td>{money(item.amount, item.currency)}</td>
          <td>{money(item.creditedAmount, item.currency)}</td>
          <td>{money(item.paidAmount, item.currency)}</td>
          <td>{money(item.outstanding, item.currency)}</td>
          <td>{money(item.refundDue, item.currency)}</td>
          <td>{item.status}</td>
          <td><button type="button" onClick={() => openInvoice(item.id)}>Open invoice</button></td>
        </tr>)}</tbody></table></div> :
        <p className="empty-state">No invoices linked to this customer.</p>}
      <div className="pagination">
        <span>Page {ledger.page} · {ledger.total} invoices</span>
        <button type="button" disabled={loading || ledger.page <= 1}
          onClick={() => void loadLedger(selected!, ledger.page - 1)}>Previous</button>
        <button type="button" disabled={loading || ledger.page * 25 >= ledger.total}
          onClick={() => void loadLedger(selected!, ledger.page + 1)}>Next</button>
      </div>
    </>}
  </section>;
}
