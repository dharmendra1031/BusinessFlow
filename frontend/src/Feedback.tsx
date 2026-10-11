import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { Icon } from './Icon';

type Level = 'success' | 'error' | 'warning' | 'info';
type Notice = { id: number; level: Level; title: string; detail?: string };
type ConfirmOptions = { title: string; message: string; confirmText?: string; danger?: boolean };
type Confirmation = ConfirmOptions & { resolve: (accepted: boolean) => void };
type FeedbackApi = {
  notify: (level: Level, title: string, detail?: string) => void;
  confirm: (options: ConfirmOptions) => Promise<boolean>;
};

const FeedbackContext = createContext<FeedbackApi | null>(null);

export function useFeedback() {
  const value = useContext(FeedbackContext);
  if (!value) throw new Error('FeedbackProvider is missing');
  return value;
}

export function FeedbackProvider({ children }: { children: React.ReactNode }) {
  const [notices, setNotices] = useState<Notice[]>([]);
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
  const nextId = useRef(0);
  const timers = useRef<ReturnType<typeof setTimeout>[]>([]);
  const dialogRef = useRef<HTMLDivElement>(null);
  const activeDialog = useRef<Confirmation | null>(null);

  const dismiss = useCallback((id: number) => {
    setNotices(current => current.filter(notice => notice.id !== id));
  }, []);

  const notify = useCallback((level: Level, title: string, detail?: string) => {
    const id = ++nextId.current;
    setNotices(current => [...current.slice(-3), { id, level, title, detail }]);
    const timer = setTimeout(() => dismiss(id), level === 'error' ? 8000 : 5000);
    timers.current.push(timer);
  }, [dismiss]);

  const confirm = useCallback((options: ConfirmOptions) =>
    new Promise<boolean>(resolve => {
      activeDialog.current?.resolve(false);
      const next = { ...options, resolve };
      activeDialog.current = next;
      setConfirmation(next);
    }), []);

  const closeDialog = useCallback((accepted: boolean) => {
    const current = activeDialog.current;
    activeDialog.current = null;
    setConfirmation(null);
    current?.resolve(accepted);
  }, []);

  useEffect(() => () => {
    timers.current.forEach(clearTimeout);
    activeDialog.current?.resolve(false);
    activeDialog.current = null;
  }, []);

  useEffect(() => {
    if (!confirmation) return;
    const previousFocus = document.activeElement as HTMLElement | null;
    dialogRef.current?.focus();
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') closeDialog(false);
      if (event.key !== 'Tab' || !dialogRef.current) return;
      const controls = Array.from(dialogRef.current.querySelectorAll<HTMLButtonElement>('button:not(:disabled)'));
      if (!controls.length) return;
      const first = controls[0];
      const last = controls[controls.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previousFocus?.focus();
    };
  }, [confirmation, closeDialog]);

  return <FeedbackContext.Provider value={{ notify, confirm }}>
    {children}
    <div className="toast-stack" role="region" aria-label="Notifications">
      {notices.map(notice => <div key={notice.id} className={'toast toast-' + notice.level} role="status">
        <span className="toast-symbol"><Icon name={notice.level === 'success' ? 'check' : notice.level === 'error' ? 'close' : 'info'} size={19} /></span>
        <div className="toast-copy"><strong>{notice.title}</strong>{notice.detail && <p>{notice.detail}</p>}</div>
        <button className="icon-button toast-dismiss" aria-label="Dismiss notification" onClick={() => dismiss(notice.id)}>
          <Icon name="close" size={17} />
        </button>
      </div>)}
    </div>
    {confirmation && <div className="dialog-backdrop" onMouseDown={event => {
      if (event.currentTarget === event.target) closeDialog(false);
    }}>
      <div className="confirm-dialog" role="alertdialog" aria-modal="true"
        aria-labelledby="dialog-title" aria-describedby="dialog-description" tabIndex={-1} ref={dialogRef}>
        <span className={'dialog-symbol ' + (confirmation.danger ? 'dialog-danger' : 'dialog-primary')}>
          <Icon name={confirmation.danger ? 'warning' : 'info'} size={24} />
        </span>
        <h2 id="dialog-title">{confirmation.title}</h2>
        <p id="dialog-description">{confirmation.message}</p>
        <div className="dialog-actions">
          <button autoFocus onClick={() => closeDialog(false)}>Cancel</button>
          <button className={confirmation.danger ? 'danger-button' : 'primary'}
            onClick={() => closeDialog(true)}>{confirmation.confirmText || 'Confirm'}</button>
        </div>
      </div>
    </div>}
  </FeedbackContext.Provider>;
}
