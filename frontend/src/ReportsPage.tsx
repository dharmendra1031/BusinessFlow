import { employeeLookup } from './employeeLookup';
import { useEffect, useState } from 'react';
import { api, apiBlob } from './api';
import { handleFormKeyDown } from './formUtils';
import { Icon } from './Icon';
import { useFeedback } from './Feedback';
import type { Employee, Lookup, ReportResult, User } from './types';

const reportOptions = [
  ['customers', 'Customer List'], ['user-customers', 'User-wise Customers'],
  ['active', 'Active Leads'], ['closed', 'Closed Leads'],
  ['not-interested', 'Not Interested'], ['followups', 'Follow-up Report'],
  ['overdue', 'Overdue Follow-ups'], ['project', 'Project Amount'],
  ['user-project', 'User-wise Project Amount'], ['created', 'Customer Creation'],
  ['source-summary', 'Source-wise Summary (RDLC)'], ['source-details', 'Source-wise Lead Details (RDLC)']
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
  const [sources, setSources] = useState<Lookup[]>([]);
  const [sourceId, setSourceId] = useState('');

  useEffect(() => {
    void api<Lookup[]>('/api/masters/lead-statuses', token).then(setStatuses).catch(() => {});
    void api<Lookup[]>('/api/masters/lead-sources', token).then(setSources).catch(() => {});
    if (user.roles.includes('Admin') || user.roles.includes('Manager'))
      void employeeLookup(token, 'reports.view').then(setEmployees).catch(() => {});
  }, [token, user.roles]);
  const [report, setReport] = useState<ReportResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);
  const [previewUrl, setPreviewUrl] = useState('');
  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl); }, [previewUrl]);

  function filters() {
    const params = new URLSearchParams({ type });
    if (from) params.set('from', from);
    if (to) params.set('to', to);
    if (leadGivenBy) params.set('leadGivenBy', leadGivenBy);
    if (userId) params.set('userId', userId);
    if (statusId) params.set('statusId', statusId);
    if (sourceId) params.set('sourceId', sourceId);
    return params;
  }

  async function load(nextPage = 1) {
    if (from && to && from > to) {
      notify('warning', 'Invalid date range', 'From date must be before or equal to To date.');
      return;
    }
    setReport(null);
    setPreviewUrl('');
    setLoading(true);
    try {
      const params = filters();
      params.set('page', String(nextPage));
      params.set('pageSize', '50');
      const result = await api<ReportResult>('/api/reports?' + params, token);
      setReport(result);
      setPage(nextPage);
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
    if (period === 'custom') return;
    let start = new Date(today);
    if (period === 'yesterday') start.setDate(start.getDate() - 1);
    if (period === 'week') start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
    if (period === 'month') start = new Date(today.getFullYear(), today.getMonth(), 1);
    setFrom(date(start));
    setTo(date(period === 'yesterday' ? start : today));
  }

  async function exportCsv() {
    try {
      const blob = await apiBlob('/api/reports/export?' + filters(), token);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = type + '-report.csv';
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      notify('error', 'Export failed', err instanceof Error ? err.message : undefined);
    }
  }

  async function previewPdf() {
    try {
      const blob = await apiBlob('/api/reports/pdf?' + filters(), token);
      setPreviewUrl(URL.createObjectURL(blob));
    } catch (err) {
      notify('error', 'PDF preview failed', err instanceof Error ? err.message : undefined);
    }
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">BUSINESSFLOW REPORTING</span>
      <h1>Reports</h1><p>Customer and follow-up reports in KWD.</p></div></header>
    <section className="panel">
      <form className="table-toolbar" onSubmit={event => { event.preventDefault(); void load(1); }}
        onKeyDown={handleFormKeyDown}>
        <label>Report <select value={type} onChange={event => { setType(event.target.value); setReport(null); }}>
          {reportOptions.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>
        <label>Period <select onChange={event => setPeriod(event.target.value)} defaultValue="all">
          <option value="all">All dates</option><option value="today">Today</option>
          <option value="yesterday">Yesterday</option><option value="week">This week</option>
          <option value="month">This month</option><option value="custom">Custom</option>
        </select></label>
        <label>From <input type="date" max={to || undefined} value={from} onChange={event => setFrom(event.target.value)} /></label>
        <label>To <input type="date" min={from || undefined} value={to} onChange={event => setTo(event.target.value)} /></label>
        <label>Lead given by <input maxLength={150} value={leadGivenBy} onChange={event => setLeadGivenBy(event.target.value)} /></label>
        <label>Source <select aria-label="Source" value={sourceId} onChange={event => setSourceId(event.target.value)}>
          <option value="">All sources</option>
          {sources.map(source => <option key={source.id} value={source.id}>{source.name}</option>)}
        </select></label>
        <label>Status <select aria-label="Status" value={statusId} onChange={event => setStatusId(event.target.value)}>
          <option value="">All statuses</option>
          {statuses.map(status => <option key={status.id} value={status.id}>{status.name}</option>)}
        </select></label>
        {employees.length > 0 && <label>User <select value={userId} onChange={event => setUserId(event.target.value)}>
          <option value="">All permitted users</option>
          {employees.map(employee => <option key={employee.id} value={employee.id}>{employee.fullName}</option>)}
        </select></label>}
        <button type="submit" className="primary" disabled={loading}>
          <Icon name="search" size={17} /> {loading ? 'Loading…' : 'Run report'}</button>
      </form>
      <p className="field-help">{user.roles.includes('Admin') ? 'Organization-wide results' : 'Only customers permitted for your account'}</p>
      {report && <>
        <div className="panel-heading">
          <div><h2>{report.total.toLocaleString()} records</h2>
            <p>Page {report.page} of {Math.max(1, Math.ceil(report.total / report.pageSize))}</p>
          </div>
          <button className="secondary-action" disabled={!report.total} onClick={() => void exportCsv()}>Export CSV</button>
          <button className="secondary-action" onClick={() => void previewPdf()}>Preview PDF</button>
        </div>
        {report.rows.length ? <div className="table-scroll"><table>
          <thead><tr>{Object.keys(report.rows[0]).map(key => <th key={key}>{key}</th>)}</tr></thead>
          <tbody>{report.rows.map((row, index) => <tr key={index}>
            {Object.keys(report.rows[0]).map(key => <td key={key}>{String(row[key] ?? '—')}</td>)}
          </tr>)}</tbody></table></div> : <p>No records for the selected filters.</p>}
        <div className="table-toolbar">
          <button type="button" disabled={loading || page <= 1} onClick={() => void load(page - 1)}>Previous</button>
          <button type="button" disabled={loading || page * report.pageSize >= report.total} onClick={() => void load(page + 1)}>Next</button>
        </div>
        {previewUrl && <iframe title="Report PDF preview" src={previewUrl} style={{ width: '100%', height: '75vh' }} />}
      </>}
    </section>
  </section>;
}
