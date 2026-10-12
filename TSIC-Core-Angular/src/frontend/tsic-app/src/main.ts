import { environment } from './environments/environment';

// Entry point. Kept free of app imports on purpose: the app is loaded with a
// dynamic import() only after the environment values are final, because several
// modules read environment.apiUrl / staticsUrl into constants as they load.
//
// Builds whose environment file sets `runtimeConfig: true` (the Ubuntu server
// builds, scripts/Ubuntu-Config) read their API and Statics addresses from
// app-config.json, written next to index.html at publish time from that machine's
// settings. Every other build uses the addresses compiled into its environment
// file, exactly as before, and makes no extra request.
void start();

async function start(): Promise<void> {
  if ((environment as { runtimeConfig?: boolean }).runtimeConfig) {
    try {
      await applyRuntimeConfig();
    } catch (err) {
      const msg = `Could not load app-config.json: ${err instanceof Error ? err.message : String(err)}. Refusing to bootstrap.`;
      document.body.innerHTML = `<pre style="padding:2rem;color:#b00;font-family:monospace;white-space:pre-wrap;">${msg}</pre>`;
      throw err;
    }
  }
  await import('./bootstrap');
}

async function applyRuntimeConfig(): Promise<void> {
  const url = new URL('app-config.json', document.baseURI).toString();
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) {
    throw new Error(`HTTP ${res.status}`);
  }
  const cfg: unknown = await res.json();
  const apiUrl = (cfg as { apiUrl?: unknown }).apiUrl;
  const staticsUrl = (cfg as { staticsUrl?: unknown }).staticsUrl;
  if (typeof apiUrl !== 'string' || !apiUrl || typeof staticsUrl !== 'string' || !staticsUrl) {
    throw new Error('apiUrl and staticsUrl are required');
  }
  Object.assign(environment, { apiUrl, staticsUrl });
}
