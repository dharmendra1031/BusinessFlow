import { useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import { handleDialogKeyDown } from './formUtils';
import type { Lead } from './types';

export default function LeadStatusRequest({ lead, statusId, isAdmin, token, onClose, onSaved }: {
  lead: Lead; statusId: number; isAdmin: boolean; token: string; onClose: () => void; onSaved: () => Promise<void>;
}) {
  const { notify } = useFeedback();
  const [override, setOverride] = useState(false);
  const [reason, setReason] = useState('');
  const [working, setWorking] = useState(false);
  const status = statusId === 5 ? 'Won' : 'Lost';
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (working) return;
    setWorking(true);
    try {
      await api(override ? `/api/leads/${lead.id}/status` : `/api/lead-approvals/leads/${lead.id}`, token, {
        method: override ? 'PATCH' : 'POST',
        body: JSON.stringify({ statusId, rowVersion: lead.rowVersion, ...(override ? { overrideReason: reason.trim() } : {}) }),
      });
      await onSaved(); onClose();
      notify('success', override ? 'Status updated' : 'Approval requested', override
        ? `${lead.name} is now ${status}. The override reason was recorded.`
        : 'The current status stays unchanged until a different authorized approver accepts the request.');
    } catch (error) { notify('error', 'Unable to change status', error instanceof Error ? error.message : undefined); }
    finally { setWorking(false); }
  }
  return <div className="dialog-backdrop"><section className="confirm-dialog" role="dialog" aria-modal="true"
    aria-labelledby="status-request-title" onKeyDown={event => handleDialogKeyDown(event, () => { if (!working) onClose(); })}>
    <h2 id="status-request-title">Request {status} status</h2>
    <p>{lead.name}: a different authorized approver must review the request.</p>
    <form onSubmit={event => void submit(event)}>
      {isAdmin && <label><input type="checkbox" checked={override} disabled={working}
        onChange={event => setOverride(event.target.checked)} />Use Admin override</label>}
      {override && <label>Override reason<textarea autoFocus required minLength={2} maxLength={500} value={reason}
        onChange={event => setReason(event.target.value)} /><small>The status changes immediately and the reason is audited.</small></label>}
      <div className="dialog-actions"><button type="button" disabled={working} onClick={onClose}>Cancel</button>
        <button className="primary" disabled={working}>{working ? 'Saving…' : override ? 'Apply override' : 'Request approval'}</button></div>
    </form>
  </section></div>;
}
