<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { api, ApiError, friendlyError } from '../lib/api';
  import { session, toast } from '../lib/app.svelte';
  import { unencryptedRemote } from '../lib/connection';
  import { deviceName } from '../lib/devicename';
  import { ICON } from '../lib/icons';
  import { PALETTE } from '../lib/wallpapers';
  import InfoTip from './InfoTip.svelte';

  type SignMode = 'pass' | 'code' | 'forgot' | 'create';
  const RESET_SENT = 'If that address has an account, a link is on its way';

  let mode = $state<SignMode>('pass');
  let email = $state('');
  let password = $state('');
  let code = $state('');
  let confirm = $state('');
  let error = $state<string | null>(null);
  let busy = $state(false);
  let form: HTMLFormElement;

  const info = $derived(session.info);
  const plainHttp = unencryptedRemote(location);
  const HEAD: Record<SignMode, [string, string]> = {
    pass: ['Sign in', 'Use your account on this server.'],
    code: ['Sign in', 'Use your account on this server.'],
    forgot: ['Reset your password', "We'll email a reset link."],
    create: ['Create an account', 'Use it here and in the PKForge app.'],
  };

  // Sign-ups may have opened or closed since this page loaded.
  onMount(() => { void session.refreshInfo(); });

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
      } else if (mode === 'create') {
        // The server's rule and words, checked here first so a typo doesn't spend a try.
        if (password.length < 8) throw new ApiError(400, 'weak_password', '');
        if (password !== confirm) { error = "The passwords don't match."; return; }
        await session.register({ email: email.trim(), password, deviceName: deviceName(navigator.userAgent) });
      } else {
        const name = deviceName(navigator.userAgent);
        await session.signIn(mode === 'code' ? { pairingCode: code, deviceName: name } : { email: email.trim(), password, deviceName: name });
      }
    } catch (err) {
      // A reset request never says whether the address has an account.
      const quiet = mode === 'forgot' && err instanceof ApiError && err.status !== 0 && err.status !== 429;
      if (quiet) toast(RESET_SENT);
      else error = friendlyError(err);
      if (err instanceof ApiError && err.code === 'registration_closed') void session.refreshInfo();
    } finally {
      busy = false;
    }
  }
</script>

<div class="signin">
  <div class="left">
    <div class="logo">{@html ICON.ball}PKForge Bank</div>
    <div style="display:grid;gap:6px">
      <h2>{HEAD[mode][0]}</h2>
      <p class="lede">{HEAD[mode][1]}</p>
    </div>
    {#if session.signedOutReason && (mode === 'pass' || mode === 'code')}
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
      {:else if mode === 'create'}
        <div class="field"><label for="ce">Email</label><input id="ce" type="email" bind:value={email} autocomplete="username" required /></div>
        <div class="field">
          <label for="cp">Password</label>
          <input id="cp" type="password" bind:value={password} autocomplete="new-password" required aria-describedby="cphint" />
          <span class="hint" id="cphint">At least 8 characters.</span>
        </div>
        <div class="field"><label for="cc">Confirm password</label><input id="cc" type="password" bind:value={confirm} autocomplete="new-password" required /></div>
        <button class="btn primary" type="submit" style="height:40px" disabled={busy}>Create account</button>
        <div class="switch">Have an account? <button type="button" onclick={() => switchTo('pass')}>Sign in</button></div>
      {:else}
        <div class="field"><label for="em">Email</label><input id="em" type="email" bind:value={email} autocomplete="username" required /></div>
        <div class="field">
          <span style="display:flex;justify-content:space-between"><label for="pw">Password</label>{#if info?.emailEnabled !== false}<button type="button" onclick={() => switchTo('forgot')} style="background:none;border:0;padding:0;color:var(--accent-text);font-weight:600;font-size:.84rem">Forgot?</button>{/if}</span>
          <input id="pw" type="password" bind:value={password} autocomplete="current-password" required />
        </div>
        <button class="btn primary" type="submit" style="height:40px" disabled={busy}>Sign in</button>
        <div class="switch">No password set? <button type="button" onclick={() => switchTo('code')}>Use a code from the app</button></div>
        {#if info?.registrationOpen}
          <div class="switch">New here? <button type="button" onclick={() => switchTo('create')}>Create an account</button></div>
        {:else if info}
          <p class="switch" style="margin:0">Sign-ups are closed on this server. Ask its admin for an account.</p>
        {/if}
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
            Open<InfoTip text="Anyone can create an account here or in the PKForge app. Signing in adds this browser to your devices; you can remove it later from any device." />
          {:else}
            Closed<InfoTip text="This server only has accounts its owner created. Signing in adds this browser to your devices; you can remove it later from any device." />
          {/if}
        </dd>
      {/if}
    </dl>
    {#if plainHttp}
      <p class="plainnote">This connection isn't encrypted. That's fine on your home network; use HTTPS to reach the server from outside it.</p>
    {/if}
  </div>
</div>
