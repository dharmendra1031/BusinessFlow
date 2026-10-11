import { useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';
import { useFeedback } from './Feedback';
import type { Lead, LeadFollowUpHistory, Lookup } from './types';

type Props = {
  lead: Lead;
  sources: Lookup[];
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
};

export default function LeadDetails({ lead, sources, token, onClose, onSaved }: Props) {
  const { notify } = useFeedback();
  const [name, setName] = useState(lead.name);
  const [phone, setPhone] = useState(lead.phone);
  const [email, setEmail] = useState(lead.email ?? '');
  const [companyName, setCompanyName] = useState(lead.companyName ?? '');
  const [requirement, setRequirement] = useState(lead.requirement ?? '');
  const [address, setAddress] = useState(lead.address ?? '');
  const [leadGivenBy, setLeadGivenBy] = useState(lead.leadGivenBy ?? '');
  const [projectAmount, setProjectAmount] = useState(lead.projectAmount?.toString() ?? '');
  const [history, setHistory] = useState<LeadFollowUpHistory[]>([]);
  const [sourceId, setSourceId] = useState(lead.sourceId);
  const [dueAt, setDueAt] = useState('');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);
  const [tab, setTab] = useState<'details' | 'followup' | 'history'>('details');

  useEffect(() => {
    if (tab !== 'history') return;
    void api<LeadFollowUpHistory[]>('/api/leads/' + lead.id + '/followups', token)
      .then(setHistory).catch(() => notify('error', 'Unable to load follow-up history'));
  }, [tab, lead.id, token, notify]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !saving) onClose();
    }
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose, saving]);

  async function saveLead(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      await api('/api/leads/' + lead.id, token, {
        method: 'PUT',
        body: JSON.stringify({ name, phone, email: email || null, companyName, requirement, sourceId,
          address, leadGivenBy, projectAmount: projectAmount ? Number(projectAmount) : null }),
      });
      await onSaved();
      notify('success', 'Changes saved', name + ' was updated successfully.');
    } catch (err) {
      notify('error', 'Unable to update lead', err instanceof Error ? err.message : undefined);
    } finally { setSaving(false); }
  }

  async function createFollowUp(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    const date = new Date(dueAt);
    if (!Number.isFinite(date.getTime()) || date <= new Date()) {
      notify('warning', 'Check the due date', 'Please select a future date and time.');
      return;
    }
    setSaving(true);
    try {
      await api('/api/leads/' + lead.id + '/followups', token, {
        method: 'POST',
        body: JSON.stringify({ dueAtUtc: date.toISOString(), notes }),
      });
      setNotes('');
      setDueAt('');
      await onSaved();
      notify('success', 'Follow-up scheduled', 'A reminder has been saved for ' + name + '.');
    } catch (err) {
      notify('error', 'Unable to schedule follow-up', err instanceof Error ? err.message : undefined);
    } finally { setSaving(false); }
  }

  return <div className="drawer-backdrop" onMouseDown={event => {
    if (event.target === event.currentTarget && !saving) onClose();
  }}>
    <aside className="detail-drawer" role="dialog" aria-modal="true" aria-labelledby="lead-details-title">
      <div className="drawer-header"><div><span className="section-kicker">LEAD DETAILS</span>
        <h2 id="lead-details-title">{lead.name}</h2>
        <p>{lead.customerCode ?? ('Lead #' + lead.id)} · {lead.source}</p></div>
        <button className="icon-button" aria-label="Close lead details" onClick={onClose}>
          <Icon name="close" /></button>
      </div>
      <div className="lead-quick-actions">
        <a href={'tel:' + lead.phone}><Icon name="phone" size={17} /> Call</a>
        {lead.email && <a href={'mailto:' + lead.email}><Icon name="mail" size={17} /> Email</a>}
        <span className="source-tag">{lead.status}</span>
      </div>
      <div className="drawer-tabs">
        <button className={tab === 'details' ? 'selected' : ''} onClick={() => setTab('details')}>Lead information</button>
        <button className={tab === 'followup' ? 'selected' : ''} onClick={() => setTab('followup')}>New follow-up</button>
        <button className={tab === 'history' ? 'selected' : ''} onClick={() => setTab('history')}>History</button>
      </div>
      {tab === 'details' ? <form className="drawer-form" onSubmit={saveLead}>
        <label>Full name <span className="required">*</span>
          <input autoFocus required maxLength={150} value={name} onChange={event => setName(event.target.value)} /></label>
        <label>Phone number <span className="required">*</span>
          <input required maxLength={30} value={phone} onChange={event => setPhone(event.target.value)} /></label>
        <label>Email address<input type="email" maxLength={256} value={email}
          onChange={event => setEmail(event.target.value)} /></label>
        <label>Company name<input maxLength={200} value={companyName}
          onChange={event => setCompanyName(event.target.value)} /></label>
        <label>Lead source<select value={sourceId} onChange={event => setSourceId(Number(event.target.value))}>
          {sources.map(source => <option key={source.id} value={source.id}>{source.name}</option>)}
        </select></label>
        <label>Address<input maxLength={500} value={address}
          onChange={event => setAddress(event.target.value)} /></label>
        <label>Lead given by <span className="required">*</span><input required maxLength={150} value={leadGivenBy}
          onChange={event => setLeadGivenBy(event.target.value)} /></label>
        <label>Project amount (KWD)<input type="number" min="0" step="0.001" value={projectAmount}
          onChange={event => setProjectAmount(event.target.value)} /></label>
        <label>Last follow-up<input readOnly value={lead.lastFollowUpDate ?? ''} /></label>
        <label>Next follow-up<input readOnly value={lead.nextFollowUpDate ?? ''} /></label>
        <label>Requirement<textarea maxLength={2000} value={requirement}
          onChange={event => setRequirement(event.target.value)} /></label>
        <div className="drawer-actions"><button type="button" disabled={saving} onClick={onClose}>Cancel</button>
          <button className="primary" disabled={saving}>{saving ? 'Saving…' : 'Save changes'}</button></div>
      </form> : <form className="drawer-form" onSubmit={createFollowUp}>
        <p className="form-intro">Choose when to contact this lead again. The follow-up will be assigned to the lead owner.</p>
        <label>Due date and time <span className="required">*</span><input required autoFocus
          type="datetime-local" value={dueAt} onChange={event => setDueAt(event.target.value)} /></label>
        <label>Notes <span className="required">*</span><textarea required maxLength={2000}
          placeholder="What should happen on the next call?" value={notes}
          onChange={event => setNotes(event.target.value)} /></label>
        <div className="drawer-actions"><button type="button" disabled={saving} onClick={onClose}>Cancel</button>
          <button className="primary" disabled={saving}>{saving ? 'Saving…' : 'Schedule follow-up'}</button></div>
      </form> : <section className="task-list">
        {history.length === 0 ? <p className="form-intro">No follow-ups recorded.</p> :
          history.map(item => <article className="followup-row" key={item.id}>
            <div className="followup-info"><strong>{item.completedAtUtc ? 'Completed' : 'Scheduled'}</strong>
              <p>{item.notes}</p><small>{new Date(item.dueAtUtc).toLocaleString()}</small></div>
          </article>)}
      </section>}
    </aside>
  </div>;
}
