<script setup lang="ts">
import { useOffline } from '@/composables/useOffline'
import { useTransition } from '@vueuse/core'
import { WifiOff } from '@lucide/vue'
import { computed } from 'vue'

// The one offline pill for the whole app: mounted by app.ts outside Inertia's page tree, so it survives layout
// changes. Playbook: surface failed background calls — "Offline" from the browser's online state and the last
// heartbeat ping result.
const { offline } = useOffline()
const shown = useTransition(computed(() => (offline.value ? 1 : 0)), { duration: 200 })
</script>

<template>
  <div
    role="status"
    aria-live="polite"
    :aria-hidden="!offline"
    class="pointer-events-none fixed inset-x-0 top-2 z-50 flex justify-center"
    :style="{ opacity: shown }"
  >
    <span
      v-show="shown > 0"
      class="flex items-center gap-2 rounded-full bg-destructive px-3 py-1.5 text-xs font-medium text-white shadow-md"
    >
      <WifiOff class="size-3.5" aria-hidden />
      Offline — reconnecting…
    </span>
  </div>
</template>
