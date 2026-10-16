<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { ApiError, friendlyError } from './lib/api';
  import { app, bank, names, prefs, session, ui } from './lib/app.svelte';
  import { router, shownBox } from './lib/router.svelte';
  import { boxTheme } from './lib/wallpapers';
  import Shell from './components/Shell.svelte';
  import SignIn from './components/SignIn.svelte';
  import Toasts from './components/Toasts.svelte';

  onMount(() => { void session.start(); });

  // Signing in loads everything and starts polling; signing out forgets this account's Bank.
  $effect(() => {
    const state = session.state;
    untrack(() => {
      if (state === 'signedIn') {
        prefs.load().catch(() => {});
        names.load().catch(() => {});
        void app.loadBank();
        app.startPolling();
      } else if (state === 'signedOut') {
        app.reset();
      }
    });
  });

  // "Match box" follows the box on screen (Account keeps the last one viewed); sign-in shows Default.
  // While the Bank loads, the cached colors stay.
  $effect(() => {
    const t = session.state === 'signedOut' ? 'default'
      : session.state === 'signedIn' && bank.loaded ? boxTheme(bank.boxes, shownBox(router.route, ui.box, bank.boxCount)) : null;
    if (t) untrack(() => prefs.follow(t));
  });

  let barHeight = $state(0); // notice bars stack on top; the sticky sidebar starts below them

  // Keep the address bar honest about which page is showing; after signing in, go where the link pointed.
  let wanted = '/bank';
  let previous = session.state;
  $effect(() => {
    const state = session.state, page = router.route.page;
    untrack(() => {
      const opening = previous === 'unknown';
      previous = state;
      if (state === 'signedOut' && page !== 'signin') {
        if (opening) wanted = router.path; // a link opened while signed out
        router.go('/signin', true);
      } else if (state === 'signedIn' && page === 'signin') {
        router.go(wanted, true);
        wanted = '/bank';
      }
    });
  });
</script>

<div class="root" style:--bar="{barHeight}px">
  <div class="bars" bind:clientHeight={barHeight}>
    {#if app.outdated}
      <div class="banner" role="alert">
        <span>PKForge Bank was updated. Reload to continue.</span>
        <button class="btn sm primary" onclick={() => location.reload()}>Reload</button>
      </div>
    {:else if session.state === 'signedIn' && app.unverified}
      <div class="banner" role="alert">
        <span>{friendlyError(new ApiError(403, 'email_not_verified', ''))}</span>
        <button class="btn sm" onclick={() => app.resendVerification()}>Resend link</button>
      </div>
    {:else if session.state === 'signedIn' && app.offline}
      <div class="banner" role="status"><span class="dot off"></span><span>Can't reach the server. Changes are paused.</span></div>
    {:else if session.state === 'signedIn' && app.readOnly}
      <div class="banner" role="status">
        <span class="dot ro"></span><span>{app.readOnlyMessage} Viewing and downloads keep working.</span>
      </div>
    {/if}
  </div>
  {#if session.state === 'signedIn'}
    <Shell />
  {:else if session.state === 'signedOut'}
    <SignIn />
  {:else if session.startError}
    <div class="boot" role="alert">
      <p>{friendlyError(session.startError)}</p>
      <button class="btn" onclick={() => session.start()}>Try again</button>
    </div>
  {/if}
</div>
<Toasts />
