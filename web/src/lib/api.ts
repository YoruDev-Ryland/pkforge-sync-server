export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message); }
}

type Listener = (e: ApiError) => void;
const listeners = new Set<Listener>();

/** Hears about every failed call (e.g. 426 when the server was updated); returns an unsubscribe. */
export function onApiError(listener: Listener): () => void {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

type Init = { method?: string; json?: unknown; signal?: AbortSignal };

async function send(path: string, init: Init = {}): Promise<Response> {
  const headers = new Headers({ 'X-PKForge-Protocol': '1' });
  if (init.json !== undefined) headers.set('content-type', 'application/json');
  let response: Response;
  try {
    response = await fetch(path, {
      method: init.method ?? (init.json !== undefined ? 'POST' : 'GET'),
      headers, credentials: 'same-origin', signal: init.signal,
      body: init.json !== undefined ? JSON.stringify(init.json) : undefined,
    });
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e;
    throw report(new ApiError(0, 'offline', "Can't reach the server."));
  }
  if (!response.ok) {
    let code = 'http_' + response.status, message = response.statusText;
    try { const body = await response.json(); code = body.code ?? code; message = body.message ?? message; } catch { /* not JSON */ }
    throw report(new ApiError(response.status, code, message));
  }
  return response;
}

function report(e: ApiError): ApiError {
  for (const listener of listeners) listener(e);
  return e;
}

export async function api<T = void>(path: string, init?: Init): Promise<T> {
  const response = await send(path, init);
  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** The raw response, for bodies read as they arrive (the live event stream). */
export function apiResponse(path: string, init?: Init): Promise<Response> {
  return send(path, init);
}

export async function apiBytes(path: string): Promise<Uint8Array> {
  return new Uint8Array(await (await send(path)).arrayBuffer());
}

const WORDS: Record<string, string> = {
  invalid_credentials: 'Email or password is wrong.',
  rate_limited: 'Too many tries. Wait a minute and try again.',
  invalid_pairing_code: 'That code expired or was already used. Make a new one in the app.',
  offline: "Can't reach the server. Check your connection and try again.",
  email_not_verified: 'Verify your email address first. Check your inbox for the link.',
  weak_password: 'Use at least 8 characters.',
};

/** A 403 writes_disabled carries the server's own reason; this is for when it has none. */
export const WRITES_DISABLED = "This server isn't accepting changes for your account.";

/** The read-only banner's words for a writes_disabled refusal: the neutral sentence, then the server's reason. */
export function writesDisabledMessage(reason: string | null | undefined): string {
  const r = reason?.trim();
  // The server's own default says the same as the sentence; don't repeat it.
  return r && !/^This server isn't accepting changes/i.test(r) ? `This server isn't accepting changes for your account: ${r}` : WRITES_DISABLED;
}

export function friendlyError(e: unknown): string {
  if (e instanceof ApiError && e.code === 'writes_disabled') return writesDisabledMessage(e.message);
  if (e instanceof ApiError) return WORDS[e.code] ?? e.message;
  return 'Something went wrong. Try again.';
}
