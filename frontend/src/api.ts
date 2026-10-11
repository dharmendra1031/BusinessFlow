const apiUrl = import.meta.env.VITE_API_URL || 'http://localhost:5081';

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

export async function api<T>(path: string, token: string, options: RequestInit = {}): Promise<T> {
  let response: Response;
  try {
    response = await fetch(apiUrl + path, {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: 'Bearer ' + token } : {}),
        ...options.headers,
      },
    });
  } catch {
    throw new Error('Cannot reach the CRM server. Check your connection and try again.');
  }

  if (!response.ok) {
    const data: unknown = await response.json().catch(() => null);
    throw new Error(errorText(data, response.status));
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}
