<script lang="ts">
  import { WifiOff } from '@lucide/svelte'
  import { offline } from '@/lib/offline.svelte'

  // Playbook: surface failed background calls — "Offline" from the browser's online state and the last heartbeat
  // ping result. Mounted by app.ts outside Inertia's page tree, so it survives layout changes.
</script>

{#if offline.show}
  <div
    role="status"
    aria-live="polite"
    class="pointer-events-none fixed inset-x-0 top-2 z-50 flex justify-center"
  >
    <span class="flex items-center gap-2 rounded-full bg-red-600 px-3 py-1.5 text-xs font-medium text-white shadow-md">
      <WifiOff class="size-3.5" aria-hidden />
      Offline — reconnecting…
    </span>
  </div>
{/if}
