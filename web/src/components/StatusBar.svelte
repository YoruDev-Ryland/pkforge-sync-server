<script lang="ts">
  import { app, session } from '../lib/app.svelte';
  import InfoTip from './InfoTip.svelte';

  const OFFLINE = "Can't reach the server. Changes are paused; browsing keeps working, and it catches up when the connection is back.";

  const devices = $derived(session.devices.length);
  const synced = $derived(app.lastSynced?.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }));
  const count = $derived(devices ? ` · ${devices} ${devices === 1 ? 'device' : 'devices'}` : '');
  // "Live" while the server's change stream is connected: other devices' changes show up at once.
  const label = $derived(`${app.live ? 'Live' : `Synced ${synced}`}${count}`);
  const title = $derived(`${app.live ? `Live, synced ${synced}` : `Synced ${synced}`}${count}`);
  const UNVERIFIED = 'The server syncs your Bank once your email address is verified. Use the link in the email, then come back to this tab.';
  const state = $derived(app.offline ? 'off' : app.unverified ? 'unv' : app.readOnly ? 'ro' : app.syncing || !synced ? 'busy' : 'ok');
</script>

<span class="dot {state === 'unv' ? 'ro' : state}"></span>
{#if state === 'off'}
  <span class="hrow">Offline<InfoTip text={OFFLINE} label="About offline" /></span>
{:else if state === 'unv'}
  <span class="hrow">Email not verified<InfoTip text={UNVERIFIED} label="About email verification" /></span>
{:else if state === 'ro'}
  <span class="hrow">Read-only<InfoTip text={`${app.readOnlyMessage} Viewing and downloads keep working.`} label="About read-only" /></span>
{:else if state === 'busy'}
  <span class="status">Syncing…</span>
{:else}
  <span class="status" title={title}>{label}</span>
{/if}
