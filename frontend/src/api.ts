const apiUrl = import.meta.env.VITE_API_URL || '';
let csrfToken = '';
let pendingActions = 0;

// Only user-initiated writes and downloads display the blocking progress popup.
// Background reads already show loading states inside their own screens.
function requestActivity(change: number) {
  pendingActions = Math.max(0, pendingActions + change);
  window.dispatchEvent(new CustomEvent('businessflow:requests', { detail: pendingActions }));
}

export function setCsrfToken(value: string) { csrfToken = value; }

function signalExpiredSession(path: string, status: number) {
  if (status === 401 && path !== '/api/auth/login' && path !== '/api/auth/me')
    window.dispatchEvent(new Event('businessflow:session-expired'));
}

export class ApiError extends Error {
  constructor(message: string, public readonly status: number) { super(message); }
}

function errorText(data: unknown, status: number): string {
  if (!data || typeof data !== 'object') return 'Request failed (' + status + ')';
  const details = data as Record<string, unknown>;
  if (typeof details.message === 'string') return details.message;
  if (Array.isArray(details.errors)) {
    const errors = details.errors.filter((value): value is string => typeof value === 'string');
    if (errors.length) return errors.join('. ');
  }
  if (details.errors && typeof details.errors === 'object') {
    const errors = Object.values(details.errors)
      .flatMap(value => Array.isArray(value) ? value : [])
      .filter((value): value is string => typeof value === 'string');
    if (errors.length) return errors.slice(0, 3).join('. ');
  }
  if (typeof details.title === 'string') return details.title;
  return 'Request failed (' + status + ')';
}

export async function api<T>(path: string, token: string, options: RequestInit & { onVersion?: (version: string) => void } = {}): Promise<T> {
  const { onVersion, ...requestOptions } = options;
  let response: Response;
  const showProgress = !!options.method && !['GET', 'HEAD', 'OPTIONS'].includes(options.method.toUpperCase());
  if (showProgress) requestActivity(1);
  try {
    response = await fetch(apiUrl + path, {
      credentials: 'same-origin',
      ...requestOptions,
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: 'Bearer ' + token } : {}),
        ...(csrfToken && options.method && !['GET', 'HEAD'].includes(options.method.toUpperCase())
          ? { 'X-CSRF-TOKEN': csrfToken } : {}),
        ...options.headers,
      },
    });
  } catch {
    throw new Error('Cannot reach the CRM server. Check your connection and try again.');
  } finally {
    if (showProgress) requestActivity(-1);
  }

  if (!response.ok) {
    signalExpiredSession(path, response.status);
    const data: unknown = await response.json().catch(() => null);
    throw new ApiError(errorText(data, response.status), response.status);
  }
  const version = response.headers.get('X-Row-Version');
  if (version) onVersion?.(version);
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}

export async function apiBlob(path: string, token: string): Promise<Blob> {
  requestActivity(1);
  try {
  const response = await fetch(apiUrl + path, {
    credentials: 'same-origin',
    headers: token ? { Authorization: 'Bearer ' + token } : {}
  });
  if (!response.ok) {
    signalExpiredSession(path, response.status);
    const data: unknown = await response.json().catch(() => null);
    throw new ApiError(errorText(data, response.status), response.status);
  }
  return await response.blob();
  } finally { requestActivity(-1); }
}
