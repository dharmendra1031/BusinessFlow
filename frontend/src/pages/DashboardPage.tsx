import { useState } from 'react';
import { Icon } from '../Icon';
import type { Dashboard, FollowUp, Lead, Paged } from '../types';
import { dateLabel, EmptyState, ErrorState, StatusBadge, useResource } from './Shared';

export function DashboardPage({ token, onNavigate }: {
  token: string;
  onNavigate: (screen: 'leads' | 'followups') => void;
}) {
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function changeFrom(value: string) {
    setFrom(value);
    if (value && to && value > to) setTo(value);
  }

  function changeTo(value: string) {
    setTo(value);
    if (value && from && value < from) setFrom(value);
  }

  function setPeriod(value: string) {
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const date = (day: Date) => [day.getFullYear(),
      String(day.getMonth() + 1).padStart(2, '0'),
      String(day.getDate()).padStart(2, '0')].join('-');
    if (value === 'all' || value === 'custom') {
      if (value === 'all') { setFrom(''); setTo(''); }
      return;
    }
    let start = new Date(today);
    if (value === 'yesterday') start.setDate(start.getDate() - 1);
    if (value === 'week') start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
    if (value === 'month') start = new Date(today.getFullYear(), today.getMonth(), 1);
    setFrom(date(start));
    setTo(date(value === 'yesterday' ? start : today));
  }

  const filters = new URLSearchParams();
  if (from) filters.set('from', from);
  if (to) filters.set('to', to);
  const path = '/api/dashboard' + (filters.size ? '?' + filters : '');
  const { data, error, loading, refresh } = useResource<Dashboard>(path, token);
  const { data: recent } = useResource<{ items: Lead[] }>('/api/leads?page=1&pageSize=5', token);
  const { data: followups } = useResource<Paged<FollowUp>>('/api/followups?view=pending&pageSize=4', token);
  const due = (followups?.items ?? []).filter(item => !item.completedAtUtc)
    .slice(0, 4);

  const metrics = [
    { label: 'Total leads', value: data?.totalLeads, icon: 'clipboard', tone: 'blue', helper: 'Across your workspace' },
    { label: 'New leads', value: data?.newLeads, icon: 'plus', tone: 'violet', helper: 'Awaiting first contact' },
    { label: 'Won leads', value: data?.wonLeads, icon: 'check', tone: 'green', helper: 'Successfully converted' },
    { label: 'Overdue follow-ups', value: data?.overdueFollowUps, icon: 'clock', tone: 'orange', helper: 'Need your attention' },
    { label: 'Project amount (KWD)', value: data?.totalProjectAmount, icon: 'clipboard', tone: 'green', helper: 'Expected project value' },
    { label: "Today's follow-ups", value: data?.todayFollowUps, icon: 'calendar', tone: 'blue', helper: 'Due today' },
  ] as const;
  const total = data?.totalLeads ?? 0;
  const won = data?.wonLeads ?? 0;
  const lost = data?.lostLeads ?? 0;
  const active = Math.max(0, total - won - lost);
  const stages = [
    { label: 'Active', value: active, tone: 'violet' },
    { label: 'Won', value: won, tone: 'green' },
    { label: 'Lost', value: lost, tone: 'orange' },
  ];

  return <section>
    <header className="page-header">
      <div><span className="section-kicker">OVERVIEW</span><h1>Good to see you!</h1>
        <p>Here's what is happening with your leads.</p></div>
      <div className="table-toolbar">
        <label>Lead creation period<select defaultValue="all" onChange={event => setPeriod(event.target.value)}>
          <option value="all">All dates</option><option value="today">Today</option>
          <option value="yesterday">Yesterday</option><option value="week">This week</option>
          <option value="month">This month</option><option value="custom">Custom</option>
        </select></label>
        <input aria-label="From date" type="date" value={from} onChange={event => changeFrom(event.target.value)} />
        <input aria-label="To date" type="date" value={to} onChange={event => changeTo(event.target.value)} />
        <button className="secondary-action" onClick={() => void refresh()} disabled={loading}>
          <Icon name="refresh" size={17} /> Refresh</button>
      </div>
    </header>
    {error && <ErrorState message={error} retry={() => void refresh()} />}
    <div className="metrics">
      {metrics.map(metric => <article className="metric" key={metric.label}>
        <div className="metric-top"><span>{metric.label}</span>
          <span className={'metric-icon tone-' + metric.tone}><Icon name={metric.icon} size={20} /></span>
        </div>
        <strong>{loading && !data ? '—' : (metric.value ?? 0).toLocaleString()}</strong>
        <small>{metric.helper}</small>
      </article>)}
    </div>
    <div className="dashboard-grid">
      <section className="panel pipeline-card">
        <div className="panel-heading"><div><h2>Lead pipeline</h2><p>How your opportunities are progressing</p></div>
          <button className="text-action" onClick={() => onNavigate('leads')}>View leads <Icon name="arrow" size={16} /></button>
        </div>
        {total === 0 ? <EmptyState title="Your pipeline starts here"
          detail="Add your first lead to start tracking progress." /> : <>
          <div className="pipeline-bar" aria-label="Lead pipeline breakdown">
            {stages.filter(stage => stage.value > 0).map(stage =>
              <div key={stage.label} className={'pipeline-segment bar-' + stage.tone}
                style={{ width: (stage.value / total * 100) + '%' }} title={stage.label + ': ' + stage.value} />)}
          </div>
          <div className="pipeline-stats">{stages.map(stage => <div key={stage.label}>
            <span className={'legend-dot bar-' + stage.tone} /><span>{stage.label}</span>
            <strong>{stage.value.toLocaleString()}</strong>
          </div>)}</div>
        </>}
      </section>
      <section className="panel followup-card">
        <div className="panel-heading"><div><h2>Next follow-ups</h2><p>Stay on top of every conversation</p></div>
          <button className="text-action" onClick={() => onNavigate('followups')}>View all <Icon name="arrow" size={16} /></button></div>
        {due.length === 0 ? <EmptyState title="Nothing pending" detail="New follow-ups will appear here." /> :
          <div className="task-list">{due.map(item => <div className="task" key={item.id}>
            <span className="task-avatar">{item.leadName.slice(0, 1).toUpperCase()}</span>
            <div><strong>{item.leadName}</strong><small>{item.notes}</small></div>
            <span className={'task-date ' + (new Date(item.dueAtUtc) < new Date() ? 'overdue-text' : '')}>
              {dateLabel(item.dueAtUtc)}</span></div>)}</div>}
      </section>
    </div>
    <section className="panel recent-panel">
      <div className="panel-heading"><div><h2>Recent leads</h2><p>Latest opportunities in your workspace</p></div>
        <button className="text-action" onClick={() => onNavigate('leads')}>View all <Icon name="arrow" size={16} /></button>
      </div>
      {recent?.items.length ? <div className="table-scroll"><table><thead><tr><th>Customer</th>
        <th>Phone</th><th>Source</th><th>Status</th><th>Owner</th></tr></thead><tbody>
        {recent.items.map(lead => <tr key={lead.id}><td><strong className="cell-name">{lead.name}</strong>
          <small>{lead.companyName || 'Individual'}</small></td><td>{lead.phone}</td><td>{lead.source}</td>
          <td><StatusBadge status={lead.status} /></td><td>{lead.assignedTo || 'Unassigned'}</td></tr>)}
      </tbody></table></div> : <EmptyState title="No leads yet" detail="Create a lead to see it here." />}
    </section>
    {!!data?.userWise?.length && <section className="panel">
      <div className="panel-heading"><div><h2>User-wise performance</h2>
        <p>Customer count and expected project amount by owner</p></div></div>
      <div className="table-scroll"><table><thead><tr><th>Employee</th><th>Customers</th>
        <th>Project amount (KWD)</th></tr></thead><tbody>
        {data.userWise.map(item => <tr key={item.userId ?? 0}>
          <td>{item.userName}</td><td>{item.customers}</td>
          <td>{item.amount.toLocaleString('en-KW')}</td>
        </tr>)}
      </tbody></table></div>
    </section>}
  </section>;
}
