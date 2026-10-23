import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';

type Duplicate = {
  firstId: number; firstName: string; firstPhone: string; firstEmail?: string;
  firstCustomerCode?: string; secondId: number; secondName: string;
  secondPhone: string; secondEmail?: string; secondCustomerCode?: string;
  matchPhone: boolean; matchEmail: boolean; decision: string; comment?: string;
  reviewedAtUtc?: string;
};
type Result = { items: Duplicate[]; total: number; page: number; pageSize: number };

export default function LeadDuplicatesPage({ token }: { token: string }) {
  const { notify } = useFeedback();
  const [state, setState] = useState<'Pending' | 'Reviewed'>('Pending');
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<Result | null>(null);
  const [selected, setSelected] = useState<Duplicate | null>(null);
  const [decision, setDecision] = useState('NotDuplicate');
  const [comment, setComment] = useState('');
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    const rows = await api<Result>('/api/admin/lead-duplicates?state=' + state + '&page=' + page, token);
    setResult(rows);
    setError('');
  }, [state, page, token]);
  useEffect(() => {
    void load().catch(err => setError(err instanceof Error ? err.message : 'Unable to load duplicate reviews.'));
  }, [load]);

  async function review(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected || working || comment.trim().length < 5) return;
    setWorking(true);
    try {
      await api('/api/admin/lead-duplicates/' + selected.firstId + '/' + selected.secondId,
        token, { method: 'POST', body: JSON.stringify({ decision, comment: comment.trim() }) });
      setSelected(null);
      setComment('');
      await load();
      notify('success', 'Duplicate review recorded',
        'No leads, assignments or follow-ups were merged or deleted.');
    } catch (err) {
      notify('error', 'Unable to record review', err instanceof Error ? err.message : undefined);
    } finally {
      setWorking(false);
    }
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">LEAD QUALITY</span>
      <h1>Duplicate lead review</h1>
      <p>Review possible matches by phone or email. Leads and their history are never merged automatically.</p>
    </div></header>
    <section className="panel" aria-label="Duplicate lead matches">
      <div className="panel-heading"><h2>Potential duplicate pairs</h2></div>
      <label>Review status <select value={state} onChange={event => {
        setState(event.target.value as 'Pending' | 'Reviewed');
        setPage(1);
        setSelected(null);
      }}><option value="Pending">Pending review</option>
        <option value="Reviewed">Reviewed</option></select></label>
      {error && <p role="alert">{error}</p>}
      {!result && !error && <p role="status">Loading potential matches…</p>}
      {result && <><div className="table-scroll"><table>
        <thead><tr><th>First lead</th><th>Matching lead</th><th>Matched on</th>
          <th>Review</th></tr></thead>
        <tbody>{result.items.map(item => <tr key={item.firstId + '-' + item.secondId}>
          <td><strong>{item.firstName}</strong>
            <div>{item.firstCustomerCode ?? '#' + item.firstId}</div>
            <div>{item.firstPhone}</div><div>{item.firstEmail ?? ''}</div></td>
          <td><strong>{item.secondName}</strong>
            <div>{item.secondCustomerCode ?? '#' + item.secondId}</div>
            <div>{item.secondPhone}</div><div>{item.secondEmail ?? ''}</div></td>
          <td>{[item.matchPhone ? 'Phone' : '', item.matchEmail ? 'Email' : '']
            .filter(Boolean).join(' & ')}</td>
          <td>{state === 'Pending'
            ? <button type="button" disabled={working} onClick={() => {
              setSelected(item); setComment(''); setDecision('NotDuplicate');
            }}>Review pair {item.firstId}-{item.secondId}</button>
            : <><strong>{item.decision === 'Confirmed' ? 'Confirmed duplicate' : 'Not duplicate'}</strong>
              <div>{item.comment}</div></>}</td>
        </tr>)}</tbody></table></div>
        {result.items.length === 0 && <p>No {state.toLowerCase()} duplicate pairs found.</p>}
        <div className="pagination"><span>Page {page} · {result.total} pairs</span>
          <div><button type="button" disabled={page <= 1}
            onClick={() => setPage(current => current - 1)}>Previous</button>
            <button type="button" disabled={page * result.pageSize >= result.total}
              onClick={() => setPage(current => current + 1)}>Next</button></div>
        </div>
      </>}
    </section>
    {selected && <section className="panel" aria-label="Review selected pair">
      <div className="panel-heading"><h2>Review {selected.firstName} and {selected.secondName}</h2></div>
      <form className="form-grid" onSubmit={event => void review(event)}>
        <label>Decision <select value={decision} onChange={event => setDecision(event.target.value)}>
          <option value="NotDuplicate">Not duplicate</option>
          <option value="Confirmed">Confirmed duplicate (no merge)</option>
        </select></label>
        <label>Review note <textarea required minLength={5} maxLength={500}
          value={comment} onChange={event => setComment(event.target.value)} /></label>
        <p>This records an auditable decision only. Both customer records, owners, and history remain unchanged.</p>
        <div><button className="primary" disabled={working || comment.trim().length < 5}>
          Save review</button>
          <button type="button" disabled={working} onClick={() => setSelected(null)}>Cancel</button></div>
      </form>
    </section>}
  </section>;
}
