import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import type { Paged } from './types';

type Approval = {
  id: number; invoiceId: number; kind: string; amount: number; paymentId: number | null;
  method: string | null; reference: string | null; reason: string;
  effectiveOn: string; requestedById: number; requestedAtUtc: string;
  state: string; canDecide: boolean; rowVersion: string;
};

export default function FinancialApprovalsSection({ token, refreshKey, onDecision }: {
  token: string; refreshKey: number; onDecision: (invoiceId: number) => void;
}) {
  const { notify, confirm } = useFeedback();
  const [items, setItems] = useState<Approval[]>([]);
  const [total, setTotal] = useState(0);
  const [reviewId, setReviewId] = useState<number | null>(null);
  const [comment, setComment] = useState('');
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await api<Paged<Approval>>('/api/admin/billing/adjustments?pageSize=20', token);
      setItems(result.items);
      setTotal(result.total);
    } catch (err) {
      notify('error', 'Cannot load financial approvals', err instanceof Error ? err.message : undefined);
    } finally { setLoading(false); }
  }, [token, notify]);

  useEffect(() => { void load(); }, [load, refreshKey]);

  async function decide(item: Approval, approve: boolean) {
    if (busy || !item.canDecide || comment.trim().length < 10) return;
    const approved = await confirm({
      title: approve ? 'Approve financial adjustment?' : 'Reject financial adjustment?',
      message: approve ? 'This posts the approved amount to financial records.' :
        'This will reject the request without changing invoice amounts.',
      confirmText: approve ? 'Approve adjustment' : 'Reject adjustment',
    });
    if (!approved) return;
    setBusy(true);
    try {
      await api('/api/admin/billing/adjustments/' + item.id + '/decision', token, {
        method: 'POST', body: JSON.stringify({
          approve, comment: comment.trim(), rowVersion: item.rowVersion,
        }),
      });
      setReviewId(null); setComment('');
      await load();
      onDecision(item.invoiceId);
      notify('success', approve ? 'Financial adjustment approved' : 'Request rejected');
    } catch (err) {
      notify('error', 'Cannot review financial adjustment', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  return <section className="panel" aria-label="Financial adjustment approvals">
    <div className="panel-heading">
      <h2>Credit &amp; refund approvals</h2>
      <p>{total} pending · two different Admin accounts required</p>
    </div>
    <button type="button" disabled={loading} onClick={() => void load()}>Refresh approvals</button>
    {loading ? <p role="status">Loading approvals…</p> :
      !items.length ? <p className="empty-state">No pending adjustment requests.</p> :
      items.map(item => <div className="project-section" key={item.id}>
        <h3>{item.kind} request #{item.id} · Invoice INV-{item.invoiceId}</h3>
        <p>Amount: {item.amount.toFixed(3)} · Requested by user #{item.requestedById} ·
          {item.effectiveOn}</p>
        <p>{item.reason}</p>
        {item.reference && <p>Reference: {item.reference}</p>}
        {item.canDecide ? <>
          <button type="button" disabled={busy} onClick={() => {
            setReviewId(item.id); setComment('');
          }}>Review request</button>
          {reviewId === item.id && <div className="form-grid">
            <label>Review comment *<textarea required minLength={10} maxLength={500}
              value={comment} onChange={event => setComment(event.target.value)} /></label>
            <div className="drawer-actions">
              <button type="button" className="primary" disabled={busy || comment.trim().length < 10}
                onClick={() => void decide(item, true)}>Approve</button>
              <button type="button" disabled={busy || comment.trim().length < 10}
                onClick={() => void decide(item, false)}>Reject</button>
            </div>
          </div>}
        </> : <p className="field-help">You submitted this request. Another Admin must review it.</p>}
      </div>)}
  </section>;
}
