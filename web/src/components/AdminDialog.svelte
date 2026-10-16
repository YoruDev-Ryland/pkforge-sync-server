<script lang="ts" module>
  import type { AdminUser } from '../lib/protocol';

  /** What the admin is about to do; `password` shows a generated password once. */
  export type AdminAction =
    | { kind: 'add' }
    | { kind: 'reset'; user: AdminUser }
    | { kind: 'signout'; user: AdminUser }
    | { kind: 'delete'; user: AdminUser };
</script>

<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { admin, confirmsEmail } from '../lib/admin.svelte';
  import { ApiError, friendlyError } from '../lib/api';
  import { session, toast } from '../lib/app.svelte';
  import { copyText } from '../lib/pairing';

  /**
   * The users list's confirmations, and the one place a generated password is shown. Laid out like the release
   * dialog: the safe button is focused, Esc cancels and focus stays inside until it closes.
   */
  let { action, close }: { action: AdminAction; close: () => void } = $props();

  let email = $state('');
  let makeAdmin = $state(false);
  let signOutToo = $state(false);
  let typed = $state('');
  let busy = $state(false);
  let error = $state('');
  /** Set once the server made a password: the dialog then only shows it. */
  let shown = $state<{ email: string; password: string; created: boolean; signedOut: boolean } | null>(null);
  let dialog: HTMLDivElement;

  const target = $derived(action.kind === 'add' ? null : action.user);
  const canDelete = $derived(action.kind === 'delete' && confirmsEmail(typed, action.user.email));

  onMount(() => {
    const back = document.activeElement as HTMLElement | null;
    first();
    return () => back?.focus?.({ preventScroll: true });
  });

  /** The field to fill, else the safe button. */
  function first() {
    (dialog.querySelector<HTMLElement>('input[type=email], input[type=text], [data-safe]'))?.focus();
  }

  function cancel() { if (!busy && !shown) close(); }

  function keydown(e: KeyboardEvent) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); cancel(); return; }
    if (e.key !== 'Tab') return;
    const items = [...dialog.querySelectorAll<HTMLElement>('button:not(:disabled), input')];
    const at = items.indexOf(document.activeElement as HTMLElement);
    const next = (at + (e.shiftKey ? -1 : 1) + items.length) % items.length;
    e.preventDefault();
    items[next].focus();
  }

  async function run(e: SubmitEvent) {
    e.preventDefault();
    if (busy) return;
    if (action.kind === 'delete' && !canDelete) { error = "That's not their email."; return; }
    busy = true;
    error = '';
    try {
      switch (action.kind) {
        case 'add': {
          const r = await admin.addUser(email, makeAdmin);
          shown = { email: r.user.email, password: r.password, created: true, signedOut: false };
          break;
        }
        case 'reset':
          shown = { email: action.user.email, password: await admin.resetPassword(action.user.id, signOutToo), created: false, signedOut: signOutToo };
          break;
        case 'signout':
          await admin.signOutDevices(action.user.id);
          toast(`Signed out ${action.user.email}`);
          close();
          return;
        case 'delete':
          await admin.deleteUser(action.user.id, typed.trim());
          toast(`Deleted ${action.user.email}`);
          close();
          return;
      }
      await tick();
      dialog.querySelector<HTMLElement>('[data-safe]')?.focus();
    } catch (err) {
      error = friendlyError(err);
      // Someone took this admin's role away meanwhile: the section goes.
      if (err instanceof ApiError && err.code === 'admin_required') void session.refreshAccount();
    } finally {
      busy = false;
    }
  }

  async function copy() {
    if (shown) toast(await copyText(shown.password) ? 'Copied.' : "Couldn't copy. Select the password and copy it instead.");
  }
</script>

<svelte:window onkeydown={keydown} />
<div class="scrim" role="presentation" onclick={(e) => { if (e.target === e.currentTarget) cancel(); }}>
  <div class="modal admindlg" role="dialog" aria-modal="true" aria-labelledby="admTitle" bind:this={dialog}>
    {#if shown}
      <h3 id="admTitle">{shown.created ? 'Account added' : 'Password reset'}</h3>
      <p>Pass this password on to <b>{shown.email}</b>. It won't be shown again.{shown.signedOut ? ' Their devices were signed out.' : ''}</p>
      <span class="pwshow"><code aria-label="New password">{shown.password}</code><button class="btn sm" onclick={copy}>Copy</button></span>
      <div class="row"><button class="btn primary" data-safe onclick={close}>Done</button></div>
    {:else}
      <form onsubmit={run} aria-busy={busy} novalidate>
        {#if action.kind === 'add'}
          <h3 id="admTitle">Add a user</h3>
          <p>The server makes a password for them. You'll see it once.</p>
          <div class="field"><label for="admEmail">Email</label><input id="admEmail" type="email" bind:value={email} autocomplete="off" required /></div>
          <label class="check"><input type="checkbox" bind:checked={makeAdmin} />Admin</label>
        {:else if action.kind === 'reset'}
          <h3 id="admTitle">Reset the password of {target?.email}?</h3>
          <p>The server makes a new password. You'll see it once.</p>
          <label class="check"><input type="checkbox" bind:checked={signOutToo} />Also sign out their devices</label>
        {:else if action.kind === 'signout'}
          <h3 id="admTitle">Sign out {target?.email} everywhere?</h3>
          <p>Every device and browser of theirs is signed out. They can sign in again, and their Bank stays.</p>
        {:else}
          <h3 id="admTitle">Delete {target?.email}?</h3>
          <p>The account and its Bank are deleted from this server. Their devices keep their own copies.</p>
          <div class="field"><label for="admConfirm">Type their email to confirm</label><input id="admConfirm" type="text" bind:value={typed} autocomplete="off" autocapitalize="off" spellcheck="false" /></div>
        {/if}
        <p class="formerr" role="alert">{error}</p>
        <div class="row">
          <button class="btn" type="button" data-safe={action.kind === 'signout' || action.kind === 'reset' ? true : undefined} onclick={close} disabled={busy}>Cancel</button>
          {#if action.kind === 'add'}
            <button class="btn primary" type="submit" disabled={busy}>Add user</button>
          {:else if action.kind === 'reset'}
            <button class="btn primary" type="submit" disabled={busy}>Reset password</button>
          {:else if action.kind === 'signout'}
            <button class="btn danger solid" type="submit" disabled={busy}>Sign out</button>
          {:else}
            <button class="btn danger solid" type="submit" disabled={busy || !canDelete}>Delete account</button>
          {/if}
        </div>
      </form>
    {/if}
  </div>
</div>
