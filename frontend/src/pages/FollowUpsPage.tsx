import { useEffect, useState } from 'react';
import { api } from '../api';
import { clearFieldError, handleDialogKeyDown, handleFormKeyDown, validateForm } from '../formUtils';
import { Icon } from '../Icon';
import { useFeedback } from '../Feedback';
import type { FollowUp, Paged } from '../types';
import { dateLabel, EmptyState, ErrorState, StatusBadge, useResource } from './Shared';

export function FollowUpsPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const [tab, setTab] = useState<'pending' | 'dueSoon' | 'overdue' | 'completed' | 'all'>('pending');
  const [page, setPage] = useState(1);
  const { data, refresh, error, loading } = useResource<Paged<FollowUp>>(
    `/api/followups?view=${tab}&page=${page}&pageSize=50`, token);
  const { data: reminders, refresh: refreshReminders, error: reminderError } = useResource<{
    total: number; overdue: number; upcoming: number;
    items: (FollowUp & { assignedToId: number })[];
  }>('/api/followups/reminders?withinHours=24&pageSize=5', token);
  useEffect(() => {
    const timer = window.setInterval(() => { void refreshReminders(); }, 60_000);
    return () => window.clearInterval(timer);
  }, [refreshReminders]);
  const [working, setWorking] = useState<number | null>(null);
  const [editing, setEditing] = useState<FollowUp | null>(null);
  const [editDueAt, setEditDueAt] = useState('');
  const [editNotes, setEditNotes] = useState('');
  const now = Date.now();
  const tasks = data?.items ?? [];

  function startEdit(item: FollowUp) {
    setEditing(item);
    const due = new Date(item.dueAtUtc);
    setEditDueAt(new Date(due.getTime() - due.getTimezoneOffset() * 60000).toISOString().slice(0, 16));
    setEditNotes(item.notes);
  }

  async function saveFollowUp(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editing || !validateForm(event.currentTarget)) return;
    if (new Date(editDueAt) <= new Date()) {
      notify('warning', 'Invalid due date', 'Choose a future date and time.');
      return;
    }
    setWorking(editing.id);
    try {
      await api('/api/followups/' + editing.id, token, { method: 'PUT',
        body: JSON.stringify({ dueAtUtc: new Date(editDueAt).toISOString(), notes: editNotes }) });
      setEditing(null);
      await Promise.all([refresh(), refreshReminders()]);
      notify('success', 'Follow-up updated');
    } catch (err) {
      notify('error', 'Update failed', err instanceof Error ? err.message : undefined);
    } finally { setWorking(null); }
  }

  async function complete(item: FollowUp) {
    const accepted = await confirm({
      title: 'Complete this follow-up?',
      message: 'Mark the task for ' + item.leadName + ' as completed?',
      confirmText: 'Mark complete',
    });
    if (!accepted) return;
    setWorking(item.id);
    try {
      await api('/api/followups/' + item.id + '/complete', token, { method: 'PATCH' });
      await Promise.all([refresh(), refreshReminders()]);
      notify('success', 'Follow-up completed', item.leadName + ' was updated.');
    } catch (err) {
      notify('error', 'Could not complete follow-up', err instanceof Error ? err.message : undefined);
    } finally { setWorking(null); }
  }

  return <section><header className="page-header"><div><span className="section-kicker">YOUR TASKS</span>
    <h1>Follow-ups</h1><p>Keep every customer conversation moving forward.</p></div>
    <button className="secondary-action" onClick={() => void refresh()}><Icon name="refresh" size={17} /> Refresh</button>
  </header>
    <section className="panel followup-panel" aria-label="My follow-up reminders">
      <div className="panel-heading"><h2>My reminders · next 24 hours</h2>
        <button type="button" onClick={() => void refreshReminders()}><Icon name="refresh" size={16} /> Refresh</button>
      </div>
      {reminderError && <ErrorState message={reminderError} retry={() => void refreshReminders()} />}
      {reminders && <>
        <p role="status">{reminders.overdue} overdue · {reminders.upcoming} due soon</p>
        {reminders.items.length > 0 ? <div className="task-table">
          {reminders.items.map(item => <article className="followup-row" key={item.id}>
            <span className="task-avatar">{item.leadName[0]?.toUpperCase() || 'L'}</span>
            <div className="followup-info"><strong>{item.leadName}</strong><p>{item.notes}</p></div>
            <div className="followup-due"><Icon name="calendar" size={16} />
              <span className={new Date(item.dueAtUtc).getTime() < Date.now() ? 'overdue-text' : ''}>
                {dateLabel(item.dueAtUtc)}</span></div>
          </article>)}
        </div> : <p>No reminders due within the next 24 hours.</p>}
      </>}
      <p>Reminders refresh every minute while this page is open.</p>
    </section>
    <section className="panel followup-panel">
      <div className="tab-list" role="tablist" aria-label="Filter follow-ups">
        {(['pending', 'dueSoon', 'overdue', 'completed', 'all'] as const).map(item =>
          <button key={item} role="tab" aria-selected={tab === item} className={tab === item ? 'selected' : ''}
            onClick={() => { setTab(item); setPage(1); }}>{item === 'dueSoon' ? 'Due next 24h' : item[0].toUpperCase() + item.slice(1)}</button>)}
      </div>
      {error && <ErrorState message={error} retry={() => void refresh()} />}
      {loading && !data ? <div className="loading-state">Loading follow-ups…</div> :
        tasks.length ? <div className="task-table">{tasks.map(item => <article className="followup-row" key={item.id}>
          <span className="task-avatar">{item.leadName[0]?.toUpperCase() || 'L'}</span>
          <div className="followup-info"><strong>{item.leadName}</strong><p>{item.notes}</p></div>
          <div className="followup-due"><Icon name="calendar" size={16} />
            <span className={!item.completedAtUtc && new Date(item.dueAtUtc).getTime() < now ? 'overdue-text' : ''}>
              {dateLabel(item.dueAtUtc)}</span></div>
          <div className="followup-action">{item.completedAtUtc ? <StatusBadge status="Completed" /> : <>
            <button disabled={working === item.id} onClick={() => startEdit(item)}>Edit</button>
            <button disabled={working === item.id} onClick={() => void complete(item)}>
              <Icon name="check" size={16} /> {working === item.id ? 'Saving…' : 'Complete'}</button></>}</div>
        </article>)}</div> : !error && <EmptyState title="All caught up"
          detail={tab === 'overdue' ? 'No overdue follow-ups in this list.' :
            tab === 'dueSoon' ? 'No follow-ups due in the next 24 hours.' : 'No follow-ups in this view.'} />}
      <div className="pagination"><span>Page {page} · {data?.total ?? 0} follow-ups</span>
        <button disabled={loading || page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
        <button disabled={loading || page * 50 >= (data?.total ?? 0)} onClick={() => setPage(page + 1)}>Next</button>
      </div>
    </section>
    {editing && <div className="drawer-backdrop"><aside className="detail-drawer" role="dialog" aria-modal="true" aria-label="Edit follow-up"
      onKeyDown={event => handleDialogKeyDown(event, () => { if (working === null) setEditing(null); })}>
      <div className="drawer-header"><div><h2>Edit follow-up</h2><p>{editing.leadName}</p></div>
        <button className="icon-button" aria-label="Close" onClick={() => setEditing(null)}
          disabled={working !== null}><Icon name="close" /></button></div>
      <form className="drawer-form" onSubmit={saveFollowUp} onInput={clearFieldError}
        onKeyDown={handleFormKeyDown}>
        <label>Next follow-up<input autoFocus required type="datetime-local" value={editDueAt}
          onChange={event => setEditDueAt(event.target.value)} /></label>
        <label>Remarks<textarea required maxLength={2000} value={editNotes}
          onChange={event => setEditNotes(event.target.value)} /></label>
        <button className="primary" disabled={working !== null}>Save changes</button>
      </form>
    </aside></div>}
  </section>;
}
