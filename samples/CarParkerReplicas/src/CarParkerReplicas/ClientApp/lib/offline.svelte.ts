import { router } from '@inertiajs/svelte'

/**
 * Heartbeat the app proves its connection with (playbook: surface failed background calls). The timer resets
 * after every completed Inertia visit — that request already proved the session is alive — and a failed ping
 * turns the offline banner on until either the browser reports itself online again or a ping succeeds.
 */
export const offline = $state({ show: false })

const INTERVAL_MS = 20_000
let timer: ReturnType<typeof setTimeout> | undefined

function apply() {
  if (typeof navigator !== 'undefined' && !navigator.onLine) offline.show = true
}

async function ping() {
  if (typeof navigator !== 'undefined' && !navigator.onLine) {
    offline.show = true
  } else {
    try {
      const r = await fetch('/ping', { cache: 'no-store' })
      offline.show = !r.ok
    } catch {
      offline.show = true
    }
  }
  timer = setTimeout(() => void ping(), INTERVAL_MS)
}

// Any completed visit proves the connection, so the next heartbeat is due INTERVAL_MS from now. The subscription
// lives for the page's lifetime (module scope), so its unsubscriber is not needed.
router.on('finish', () => {
  offline.show = false
  apply()
  clearTimeout(timer)
  timer = setTimeout(() => void ping(), INTERVAL_MS)
})

const cameOnline = () => void ping() // back online: verify at once instead of waiting for the timer
const wentOffline = () => (offline.show = true)
window.addEventListener('online', cameOnline)
window.addEventListener('offline', wentOffline)
timer = setTimeout(() => void ping(), INTERVAL_MS)
