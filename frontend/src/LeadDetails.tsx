import { useState } from 'react';
import { api } from './api';
import type { Lead, Lookup } from './types';

type Props = {
  lead: Lead;
  sources: Lookup[];
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
};

export default function LeadDetails({ lead, sources, token, onClose, onSaved }: Props) {
  const [name, setName] = useState(lead.name);
  const [phone, setPhone] = useState(lead.phone);
  const [email, setEmail] = useState(lead.email ?? '');
  const [companyName, setCompanyName] = useState(lead.companyName ?? '');
  const [requirement, setRequirement] = useState(lead.requirement ?? '');
  const [sourceId, setSourceId] = useState(lead.sourceId);
  const [dueAt, setDueAt] = useState('');
  const [notes, setNotes] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  async function saveLead(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    try {
      await api('/api/leads/' + lead.id, token, {
        method: 'PUT',
        body: JSON.stringify({ name, phone, email: email || null, companyName, requirement, sourceId }),
      });
      await onSaved();
      setError('');
      setMessage('Lead updated');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to update lead');
    } finally {
      setSaving(false);
    }
  }

  async function createFollowUp(event: React.FormEvent) {
    event.preventDefault();
    const date = new Date(dueAt);
    if (!Number.isFinite(date.getTime()) || date <= new Date()) {
      setError('Choose a future date and time');
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
      setError('');
      setMessage('Follow-up scheduled');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to schedule follow-up');
    } finally {
      setSaving(false);
    }
  }

  return <div className="panel">
    <header className="page-header"><div><h2>{lead.name}</h2><p>Lead #{lead.id}</p></div>
      <button type="button" onClick={onClose}>Close</button></header>
    {error && <p className="error" role="alert">{error}</p>}
    {message && <p role="status">{message}</p>}
    <form className="form-grid" onSubmit={saveLead}>
      <label>Name<input required maxLength={150} value={name} onChange={e => setName(e.target.value)} /></label>
      <label>Phone<input required maxLength={30} value={phone} onChange={e => setPhone(e.target.value)} /></label>
      <label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} /></label>
      <label>Company<input maxLength={200} value={companyName} onChange={e => setCompanyName(e.target.value)} /></label>
      <label>Source<select value={sourceId} onChange={e => setSourceId(Number(e.target.value))}>
        {sources.map(source => <option key={source.id} value={source.id}>{source.name}</option>)}
      </select></label>
      <label className="wide">Requirement<textarea maxLength={2000} value={requirement}
        onChange={e => setRequirement(e.target.value)} /></label>
      <div className="wide"><button className="primary" disabled={saving}>Save changes</button></div>
    </form>
    <h3>Schedule follow-up</h3>
    <form className="form-grid" onSubmit={createFollowUp}>
      <label>Due date and time<input required type="datetime-local" value={dueAt}
        onChange={e => setDueAt(e.target.value)} /></label>
      <label className="wide">Notes<textarea required maxLength={2000} value={notes}
        onChange={e => setNotes(e.target.value)} /></label>
      <div className="wide"><button disabled={saving}>Add follow-up</button></div>
    </form>
  </div>;
}
