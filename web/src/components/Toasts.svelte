<script lang="ts">
  import { toasts } from '../lib/toast.svelte';
</script>

{#if toasts.message}
  {#key toasts.seq}
    <div class="toast" role="status">{toasts.message}</div>
  {/key}
{/if}
