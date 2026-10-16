<script lang="ts">
  import { onMount } from 'svelte';
  import { ago } from '../lib/ago';
  import { admin, capNote, plural } from '../lib/admin.svelte';
  import { ApiError, friendlyError } from '../lib/api';
  import { session, toast } from '../lib/app.svelte';
  import { ICON } from '../lib/icons';
  import type { AdminUser } from '../lib/protocol';
  import AdminDialog, { type AdminAction } from './AdminDialog.svelte';
  import InfoTip from './InfoTip.svelte';
  import Popover from './Popover.svelte';

  /** Server admin tools on the Account page: the sign-ups switch and every account. Only admins see it. */

  onMount(() => { void admin.load(); });

  /** Server times may come without a zone; they are UTC. */
  const utc = (s: string) => new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(s) ? s : s + 'Z');
  const note = $derived(capNote(admin.total, admin.users.length));
  const me = $derived(session.account?.id);
  /** Without email nobody can confirm an address, so the column would only ever say No. */
  const email = $derived(session.info?.emailEnabled === true);

  let action = $state<AdminAction | null>(null);
  let switching = $state(false);

  function failed(e: unknown) {
    toast(friendlyError(e));
    if (e instanceof ApiError && e.code === 'admin_required') void session.refreshAccount();
  }

  async function toggleSignups() {
    if (admin.allowRegistration === null || switching) return;
    switching = true;
    try { await admin.setAllowRegistration(!admin.allowRegistration); }
    catch (e) { failed(e); }
    finally { switching = false; }
  }

  async function setAdmin(u: AdminUser, close: () => void) {
    close();
    try {
      await admin.setAdmin(u.id, !u.isAdmin);
      toast(u.isAdmin ? `${u.email} is no longer an admin` : `${u.email} is now an admin`);
    } catch (e) { failed(e); }
  }

  function open(next: AdminAction, close: () => void) {
    close();
    action = next;
  }
</script>

<h2 class="acct-h2">Server</h2>
<p class="acct-server">Only admins see this part.</p>

<section class="setrow">
  <div><h3 class="hrow">Sign-ups<InfoTip text="When on, anyone who can reach this server can create an account, here or in the PKForge app. When off, only admins add accounts. This setting replaces ALLOW_REGISTRATION from the server's settings." /></h3></div>
  <div class="signups">
    <button class="toggle" role="switch" aria-checked={admin.allowRegistration === true} disabled={admin.allowRegistration === null || switching} onclick={toggleSignups}>
      <span class="track" aria-hidden="true"></span>Allow new accounts
    </button>
    <span class="sub">{admin.allowRegistration === null ? ' ' : admin.allowRegistration ? 'Anyone who can reach this server can sign up.' : 'Only admins can add accounts.'}</span>
  </div>
</section>

<section class="setrow usersrow">
  <div>
    <h3 class="hrow">Users<InfoTip text="Every account on this server, newest first. Pokémon counts what is in each Bank now. Last active is the latest sync from any of their devices." /></h3>
    <p>{admin.loaded ? plural(admin.total, 'account') : ' '}</p>
  </div>
  <div><button class="btn sm" onclick={() => (action = { kind: 'add' })}>{@html ICON.plus}Add user</button></div>
  <div class="users" class:noemail={!email} role="table" aria-label="Users" aria-busy={!admin.loaded && !admin.error}>
    {#if !admin.loaded}
      {#if admin.error}
        <div class="relnote" role="alert"><span>{admin.error}</span><button class="btn sm" onclick={() => admin.load()}>Try again</button></div>
      {/if}
    {:else}
      <div class="urow uhead" role="row">
        <span role="columnheader">Email</span><span role="columnheader">Admin</span>{#if email}<span role="columnheader">Confirmed</span>{/if}
        <span role="columnheader">Devices</span><span role="columnheader">Pokémon</span><span role="columnheader">Created</span>
        <span role="columnheader">Last active</span><span role="columnheader"><span class="vh">Actions</span></span>
      </div>
      {#each admin.users as u (u.id)}
        {@const self = u.id === me}
        <div class="urow" role="row">
          <span class="u-em" role="cell" title={u.email}>{u.email}{#if self}<span class="you">{' (you)'}</span>{/if}</span>
          <span class="u-c" class:none={!u.isAdmin} role="cell"><span class="w" class:no={!u.isAdmin}>{u.isAdmin ? 'Yes' : 'No'}</span><span class="n">Admin</span></span>
          {#if email}<span class="u-c" role="cell"><span class="w" class:no={!u.emailVerified}>{u.emailVerified ? 'Yes' : 'No'}</span><span class="n">{u.emailVerified ? 'Confirmed' : 'Not confirmed'}</span></span>{/if}
          <span class="u-c" role="cell"><span class="w">{u.devices}</span><span class="n">{plural(u.devices, 'device')}</span></span>
          <span class="u-c" role="cell"><span class="w">{u.mons}</span><span class="n">{plural(u.mons, 'Pokémon', 'Pokémon')}</span></span>
          <span class="u-br" aria-hidden="true"></span>
          <span class="u-d" role="cell"><span class="n">{'Created '}</span>{ago(utc(u.createdUtc))}</span>
          <span class="u-d" role="cell">{#if u.lastActiveUtc}<span class="n">{'Active '}</span>{ago(utc(u.lastActiveUtc))}{:else}<span class="w no">Never</span><span class="n">Never active</span>{/if}</span>
          <span class="u-br first" aria-hidden="true"></span>
          <span class="u-act" role="cell">
            <Popover label="Manage {u.email}" cls="umenu">
              {#snippet trigger(toggle, isOpen)}
                <button class="btn sm" aria-haspopup="true" aria-expanded={isOpen} aria-label="Manage {u.email}" onclick={toggle}>Manage</button>
              {/snippet}
              {#snippet children(close)}
                <button class="menuitem plain" onclick={() => open({ kind: 'reset', user: u }, close)}>Reset password</button>
                <button class="menuitem plain" aria-disabled={self && u.isAdmin ? 'true' : undefined}
                  onclick={() => { if (!(self && u.isAdmin)) void setAdmin(u, close); }}>{u.isAdmin ? 'Remove admin' : 'Make admin'}</button>
                <button class="menuitem plain" onclick={() => open({ kind: 'signout', user: u }, close)}>Sign out all devices</button>
                <button class="menuitem danger" aria-disabled={self ? 'true' : undefined}
                  onclick={() => { if (!self) open({ kind: 'delete', user: u }, close); }}>Delete account</button>
                {#if self}<span class="menunote">This is you. Your own admin and account can't be changed here.</span>{/if}
              {/snippet}
            </Popover>
          </span>
        </div>
      {/each}
    {/if}
  </div>
  {#if note}<p class="ucap">{note}</p>{/if}
</section>

{#if action}
  <AdminDialog {action} close={() => (action = null)} />
{/if}
