import { computed, onMounted, onUnmounted, ref, type Ref } from 'vue'
import { router } from '@inertiajs/vue3'

/**
 * Heartbeat the app proves its connection with (playbook: surface failed background calls). The timer resets
 * after every completed Inertia visit — that request already proved the session is alive — and a failed ping
 * turns the offline banner on until either the browser reports itself online again or a ping succeeds.
 */
export function useOffline(): { offline: Ref<boolean> } {
  // Starts true so a fresh page never flashes "Offline" before the first heartbeat.
  const online = ref(true)
  const pinged = ref(true)
  const INTERVAL_MS = 20_000
  let timer: ReturnType<typeof setTimeout> | undefined

  const apply = () => {
    online.value = navigator.onLine
  }

  const ping = async () => {
    apply()
    try {
      const r = await fetch('/ping', { cache: 'no-store' })
      pinged.value = r.ok
    } catch {
      pinged.value = false
    }
    timer = setTimeout(() => void ping(), INTERVAL_MS)
  }

  // Any completed visit proves the connection, so the next heartbeat is due INTERVAL_MS from now.
  const removeFinish = router.on('finish', () => {
    pinged.value = true
    apply()
    clearTimeout(timer)
    timer = setTimeout(() => void ping(), INTERVAL_MS)
  })

  const cameOnline = () => void ping() // back online: verify at once instead of waiting for the timer
  const wentOffline = () => {
    apply()
    pinged.value = false
  }
  window.addEventListener('online', cameOnline)
  window.addEventListener('offline', wentOffline)

  const start = () => {
    timer = setTimeout(() => void ping(), INTERVAL_MS)
  }
  onMounted(start)
  onUnmounted(() => {
    removeFinish()
    window.removeEventListener('online', cameOnline)
    window.removeEventListener('offline', wentOffline)
    clearTimeout(timer)
  })

  return { offline: computed(() => !online.value || !pinged.value) }
}
