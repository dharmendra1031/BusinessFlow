import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';
import { useFeedback } from './Feedback';
import type { Employee, Lookup, ReportResult, User } from './types';

const reportOptions = [
  ['customers', 'Customer List'], ['user-customers', 'User-wise Customers'],
  ['active', 'Active Leads'], ['closed', 'Closed Leads'],
  ['not-interested', 'Not Interested'], ['followups', 'Follow-up Report'],
  ['overdue', 'Overdue Follow-ups'], ['project', 'Project Amount'],
  ['user-project', 'User-wise Project Amount'], ['created', 'Customer Creation']
];

export default function ReportsPage({ token, user }: { token: string; user: User }) {
  const { notify } = useFeedback();
  const [type, setType] = useState('customers');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [leadGivenBy, setLeadGivenBy] = useState('');
  const [userId, setUserId] = useState('');
  const [statusId, setStatusId] = useState('');
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [statuses, setStatuses] = useState<Lookup[]>([]);

  useEffect(() => {
    void api<Lookup[]>('/api/masters/lead-statuses', token).then(setStatuses).catch(() => {});
    if (user.roles.includes('Admin') || user.roles.includes('Manager'))
      void api<Employee[]>('/api/employees', token).then(setEmployees).catch(() => {});
  }, [token, user.roles]);
  const [report, setReport] = useState<ReportResult | null>(null);
  const [loading, setLoading] = useState(false);

  async function load() {
    setLoading(true);
    try {
      const params = new URLSearchParams({ type });
      if (from) params.set('from', from);
      if (to) params.set('to', to);
      if (leadGivenBy) params.set('leadGivenBy', leadGivenBy);
      if (userId) params.set('userId', userId);
      if (statusId) params.set('statusId', statusId);
      const result = await api<ReportResult>('/api/reports?' + params, token);
      setReport(result);
    } catch (err) {
      notify('error', 'Report failed', err instanceof Error ? err.message : undefined);
    } finally {
      setLoading(false);
    }
  }

  function setPeriod(period: string) {
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const date = (value: Date) => [value.getFullYear(),
      String(value.getMonth() + 1).padStart(2, '0'),
      String(value.getDate()).padStart(2, '0')].join('-');
    if (period === 'all') { setFrom(''); setTo(''); return; }
    let start = new Date(today);
    if (period === 'yesterday') start.setDate(start.getDate() - 1);
    if (period === 'week') start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
    if (period === 'month') start = new Date(today.getFullYear(), today.getMonth(), 1);
    setFrom(date(start));
    setTo(date(period === 'yesterday' ? start : today));
  }

  function exportCsv() {
    if (!report?.rows.length) return;
    const keys = Object.keys(report.rows[0]);
    const escape = (value: unknown) => {
      const text = String(value ?? '');
      const safe = /^[=+\\-@\\t\\r]/.test(text) ? "'" + text : text;
      return '"' + safe.replace(/"/g, '""') + '"';
    };
    const lines = [keys.map(escape).join(','), ...report.rows.map(row =>
      keys.map(key => escape(row[key])).join(','))].join('\r\n');
    const blob = new Blob(['\uFEFF' + lines], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = type + '-report.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">CRM REPORTING</span>
      <h1>Reports</h1><p>Customer and follow-up reports in KWD.</p></div></header>
    <section className="panel">
      <div className="table-toolbar">
        <label>Report <select value={type} onChange={event => { setType(event.target.value); setReport(null); }}>
          {reportOptions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>
        <label>Period <select onChange={event => setPeriod(event.target.value)} defaultValue="all">
          <option value="all">All dates</option><option value="today">Today</option>
          <option value="yesterday">Yesterday</option><option value="week">This week</option>
          <option value="month">This month</option><option value="custom">Custom</option>
        </select></label>
        <label>From <input type="date" value={from} onChange={event => setFrom(event.target.value)} /></label>
        <label>To <input type="date" value={to} onChange={event => setTo(event.target.value)} /></label>
        <label>Lead given by <input value={leadGivenBy} onChange={event => setLeadGivenBy(event.target.value)} /></label>
        <label>Status <select value={statusId} onChange={event => setStatusId(event.target.value)}>
          <option value="">All statuses</option>
          {statuses.map(status => <option key={status.id} value={status.id}>{status.name}</option>)}
        </select></label>
        {employees.length > 0 && <label>User <select value={userId} onChange={event => setUserId(event.target.value)}>
          <option value="">All permitted users</option>
          {employees.map(employee => <option key={employee.id} value={employee.id}>{employee.fullName}</option>)}
        </select></label>}
        <button className="primary" disabled={loading} onClick={() => void load()}>
          <Icon name="search" size={17} /> {loading ? 'Loading…' : 'Run report'}</button>
      </div>
      <p className="field-help">{user.roles.includes('Admin') ? 'Organization-wide results' : 'Only customers permitted for your account'}</p>
      {report && <>
        <div className="panel-heading">
          <div><h2>{report.total.toLocaleString()} records</h2>
            {report.truncated && <p>Only the first 5,000 rows are shown. Narrow the date range.</p>}
          </div>
          <button className="secondary-action" disabled={!report.rows.length} onClick={exportCsv}>Export CSV</button>
        </div>
        {report.rows.length ? <div className="table-scroll"><table>
          <thead><tr>{Object.keys(report.rows[0]).map(key => <th key={key}>{key}</th>)}</tr></thead>
          <tbody>{report.rows.map((row, index) => <tr key={index}>
            {Object.keys(report.rows[0]).map(key => <td key={key}>{String(row[key] ?? '—')}</td>)}
          </tr>)}</tbody></table></div> : <p>No records for the selected filters.</p>}
      </>}
    </section>
  </section>;
}
