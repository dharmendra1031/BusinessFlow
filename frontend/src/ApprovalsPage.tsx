import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import type { Paged, User } from './types';

type Approval = { id: number; leadId: number; requestedStatusId: number; requestedById: number;
  requestedAtUtc: string; state: string; comment: string | null };

export default function ApprovalsPage({ token, user }: { token: string; user: User }) {
  const { notify, confirm } = useFeedback();
  const [state, setState] = useState('Pending');
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<Approval> | null>(null);
  const [comments, setComments] = useState<Record<number, string>>({});
  const [working, setWorking] = useState(false);
  const load = useCallback(async () => {
    const result = await api<Paged<Approval>>(`/api/lead-approvals?state=${state}&page=${page}&pageSize=20`, token);
    setData(result);
  }, [state, page, token]);
  useEffect(() => { void load().catch(error => notify('error', 'Unable to load approvals', error instanceof Error ? error.message : undefined)); }, [load, notify]);
  async function decide(item: Approval, approve: boolean) {
    if (working || !await confirm({ title: approve ? 'Approve status change?' : 'Reject status change?',
      message: `Lead #${item.leadId}: ${approve ? 'apply' : 'reject'} the requested ${item.requestedStatusId === 5 ? 'Won' : 'Lost'} status.`,
      confirmText: approve ? 'Approve' : 'Reject', danger: !approve })) return;
    setWorking(true);
    try {
      await api(`/api/lead-approvals/${item.id}/decision`, token, { method: 'POST', body: JSON.stringify({ approve, comment: comments[item.id] || null }) });
      await load(); notify('success', approve ? 'Request approved' : 'Request rejected');
    } catch (error) { notify('error', 'Unable to decide request', error instanceof Error ? error.message : undefined); }
    finally { setWorking(false); }
  }
  return <section><header className="page-header"><div><span className="section-kicker">SALES WORKSPACE</span>
    <h1>Lead approvals</h1><p>Review Won/Lost requests within your permitted scope. You cannot approve your own request.</p></div></header>
    <section className="panel"><div className="table-toolbar"><label>Request state<select value={state}
      onChange={event => { setState(event.target.value); setPage(1); }}>
      {['Pending', 'Approved', 'Rejected'].map(value => <option key={value}>{value}</option>)}</select></label>
      <button disabled={working} onClick={() => void load().catch(error => notify('error', 'Unable to refresh', String(error)))}>Refresh approvals</button></div>
      {!data ? <p role="status">Loading approvals…</p> : <>
        <div className="table-scroll"><table><thead><tr><th>Lead</th><th>Requested status</th><th>Requested by</th><th>Requested at</th><th>Review</th></tr></thead>
          <tbody>{data.items.map(item => <tr key={item.id}><td>Lead #{item.leadId}</td><td>{item.requestedStatusId === 5 ? 'Won' : 'Lost'}</td>
            <td>Employee #{item.requestedById}</td><td>{new Date(item.requestedAtUtc).toLocaleString()}</td><td>
              {state === 'Pending' ? <><label>Review comment<input aria-label={'Review comment ' + item.id} maxLength={500} value={comments[item.id] || ''}
                onChange={event => setComments(current => ({ ...current, [item.id]: event.target.value }))} /></label>
                <button disabled={working || item.requestedById === user.id} onClick={() => void decide(item, true)}>Approve</button>
                <button disabled={working || item.requestedById === user.id} onClick={() => void decide(item, false)}>Reject</button>
                {item.requestedById === user.id && <small>A different approver must review this request.</small>}</> : item.comment || item.state}
            </td></tr>)}</tbody></table></div>
        {data.total === 0 && <p>No {state.toLowerCase()} requests.</p>}
        <div className="table-toolbar"><span>{data.total} requests</span><button disabled={page === 1 || working} onClick={() => setPage(current => current - 1)}>Previous</button>
          <span>Page {page}</span><button disabled={page * 20 >= data.total || working} onClick={() => setPage(current => current + 1)}>Next</button></div>
      </>}
    </section></section>;
}
