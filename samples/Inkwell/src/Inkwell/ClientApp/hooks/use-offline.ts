import { useEffect, useState } from 'react'
import { router } from '@inertiajs/react'

type Connection = {
  /** The browser's navigator.onLine. */
  online: boolean
  /** Whether the last /ping reached the server. Starts true so a fresh page never flashes "Offline". */
  pinged: boolean
}

const INTERVAL_MS = 20_000

/**
 * Heartbeat the app proves its connection with (playbook: surface failed background calls). The timer resets
 * after every completed Inertia visit — that request already proved the session is alive — and a failed ping
 * turns the offline banner on until either the browser reports itself online again or a ping succeeds.
 */
export function useOffline(): boolean {
  const [state, setState] = useState<Connection>({ online: typeof navigator === 'undefined' || navigator.onLine, pinged: true })

  useEffect(() => {
    let timer: number | undefined

    const apply = (next: Connection) => setState((s) => (s.online === next.online && s.pinged === next.pinged ? s : next))

    const ping = async () => {
      try {
        const r = await fetch('/ping', { cache: 'no-store' })
        apply({ online: navigator.onLine, pinged: r.ok })
      } catch {
        apply({ online: navigator.onLine, pinged: false })
      }
      timer = window.setTimeout(() => void ping(), INTERVAL_MS)
    }

    // Any completed visit proves the connection, so the next heartbeat is due INTERVAL_MS from now.
    const finish = () => {
      apply({ online: navigator.onLine, pinged: true })
      window.clearTimeout(timer)
      timer = window.setTimeout(() => void ping(), INTERVAL_MS)
    }
    const removeFinish = router.on('finish', finish)

    const online = () => void ping() // back online: verify at once instead of waiting for the timer
    const offline = () => apply({ online: false, pinged: false })
    window.addEventListener('online', online)
    window.addEventListener('offline', offline)

    timer = window.setTimeout(() => void ping(), INTERVAL_MS)
    return () => {
      removeFinish()
      window.removeEventListener('online', online)
      window.removeEventListener('offline', offline)
      window.clearTimeout(timer)
    }
  }, [])

  return !state.online || !state.pinged
}
