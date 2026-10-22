<script lang="ts">
  import { onMount } from 'svelte'
  import { Volume2, VolumeX } from '@lucide/svelte'
  import { soundEnabled, setSoundEnabled } from '@/lib/notification-sound'
  let enabled = $state(false)
  onMount(() => { enabled = soundEnabled() })
  async function toggle() {
    try { await setSoundEnabled(!enabled); enabled = soundEnabled() } catch { enabled = false }
  }
</script>
<button type="button" class="inline-flex size-9 shrink-0 items-center justify-center rounded-md hover:bg-muted" aria-pressed={enabled} aria-label={enabled ? 'Mute notification sounds' : 'Enable notification sounds'} title={enabled ? 'Mute notification sounds' : 'Enable notification sounds'} onclick={toggle}>{#if enabled}<Volume2 class="size-4" />{:else}<VolumeX class="size-4" />{/if}</button>
