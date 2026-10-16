<script lang="ts">
  import { tick } from 'svelte';
  import { api, ApiError, friendlyError } from '../lib/api';
  import { session, toast } from '../lib/app.svelte';
  import { deviceName } from '../lib/devicename';
  import { ICON } from '../lib/icons';
  import { PALETTE } from '../lib/wallpapers';
  import InfoTip from './InfoTip.svelte';

  type SignMode = 'pass' | 'code' | 'forgot';
  const RESET_SENT = 'If that address has an account, a link is on its way';

  let mode = $state<SignMode>('pass');
  let email = $state('');
  let password = $state('');
  let code = $state('');
  let error = $state<string | null>(null);
  let busy = $state(false);
  let form: HTMLFormElement;

  const info = $derived(session.info);

  async function switchTo(next: SignMode) {
    mode = next;
    error = null;
    await tick();
    form.querySelector('input')?.focus();
  }

  /** Codes are shown as K7QM-4XTP; people type them with spaces, dashes or lowercase. */
  function typeCode(e: Event & { currentTarget: HTMLInputElement }) {
    code = e.currentTarget.value.toUpperCase().replace(/[\s-]/g, '').slice(0, 8);
    e.currentTarget.value = code;
  }

  async function submit(e: SubmitEvent) {
    e.preventDefault();
    if (busy) return;
    busy = true;
    error = null;
    try {
      if (mode === 'forgot') {
        await api('/v1/auth/password-reset', { json: { email: email.trim() } });
        toast(RESET_SENT);
      } else {
        const name = deviceName(navigator.userAgent);
        await session.signIn(mode === 'code' ? { pairingCode: code, deviceName: name } : { email: email.trim(), password, deviceName: name });
      }
    } catch (err) {
      // A reset request never says whether the address has an account.
      const quiet = mode === 'forgot' && err instanceof ApiError && err.status !== 0 && err.status !== 429;
      if (quiet) toast(RESET_SENT);
      else error = friendlyError(err);
    } finally {
      busy = false;
    }
  }
</script>

<div class="signin">
  <div class="left">
    <div class="logo">{@html ICON.ball}PKForge Bank</div>
    <div style="display:grid;gap:6px">
      <h2>{mode === 'forgot' ? 'Reset your password' : 'Sign in'}</h2>
      <p class="lede">{mode === 'forgot' ? "We'll email a reset link." : 'Use the account you created in the app.'}</p>
    </div>
    {#if session.signedOutReason && mode !== 'forgot'}
      <p class="signnote" role="status">{session.signedOutReason}</p>
    {/if}
    <form bind:this={form} onsubmit={submit} aria-busy={busy}>
      {#if mode === 'code'}
        <div class="field">
          <span class="hrow"><label for="pc">Code from the app</label><InfoTip text="In PKForge open Settings › Bank Sync › Add device. The code works once, for 5 minutes." /></span>
          <input class="code" id="pc" value={code} oninput={typeCode} autocomplete="one-time-code" autocapitalize="characters" spellcheck="false" required />
        </div>
        <button class="btn primary" type="submit" style="height:40px" disabled={busy}>Sign in</button>
        <div class="switch">Have a password? <button type="button" onclick={() => switchTo('pass')}>Sign in with email</button></div>
      {:else if mode === 'forgot'}
        <div class="field"><label for="fe">Email</label><input id="fe" type="email" bind:value={email} autocomplete="username" required /></div>
        <button class="btn primary" type="submit" style="height:40px" disabled={busy}>Email me a reset link</button>
        <div class="switch"><button type="button" onclick={() => switchTo('pass')}>Back to sign in</button></div>
      {:else}
        <div class="field"><label for="em">Email</label><input id="em" type="email" bind:value={email} autocomplete="username" required /></div>
        <div class="field">
          <span style="display:flex;justify-content:space-between"><label for="pw">Password</label>{#if info?.emailEnabled !== false}<button type="button" onclick={() => switchTo('forgot')} style="background:none;border:0;padding:0;color:var(--accent-text);font-weight:600;font-size:.84rem">Forgot?</button>{/if}</span>
          <input id="pw" type="password" bind:value={password} autocomplete="current-password" required />
        </div>
        <button class="btn primary" type="submit" style="height:40px" disabled={busy}>Sign in</button>
        <div class="switch">No password set? <button type="button" onclick={() => switchTo('code')}>Use a code from the app</button></div>
      {/if}
    </form>
    <!-- Always present (empty when fine) so an error neither shifts the form nor goes unannounced. -->
    <p class="formerr" role="alert">{error ?? ''}</p>
  </div>
  <div class="right">
    <!-- An art wallpaper reads clearly in both modes (a palette color alone nearly vanished under the dark veil). -->
    <div class="preview" aria-hidden="true" style:background-color={PALETTE[0]} style:background-image='url("/pkhex/wallpapers/box_wp01e.png")' style:background-size="cover" style:image-rendering="pixelated">
      {#each { length: 30 }, i (i)}<span class="slot"></span>{/each}
    </div>
    <dl class="serverinfo">
      <dt>Server</dt><dd>{info ? `${info.name} · ` : ''}{location.host}</dd>
      {#if info}
        <dt>Sign-ups</dt>
        <dd class="hrow">
          {#if info.registrationOpen}
            Open<InfoTip text="Anyone can create an account in the PKForge app. Signing in adds this browser to your devices; you can remove it later from any device." />
          {:else}
            Closed<InfoTip text="This server only has accounts its owner created. Signing in adds this browser to your devices; you can remove it later from any device." />
          {/if}
        </dd>
      {/if}
    </dl>
  </div>
</div>
