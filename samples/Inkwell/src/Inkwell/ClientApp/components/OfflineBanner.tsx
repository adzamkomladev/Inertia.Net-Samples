import { useOffline } from '@/hooks/use-offline'
import { cn } from 'cn'
import { WifiOff } from 'lucide-react'

/** A small fixed pill that appears whenever the browser is offline or the heartbeat ping fails (playbook:
 * surface failed background calls — "Offline" from the browser's online state and the last ping result). */
export function OfflineBanner() {
  const offline = useOffline()
  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        'pointer-events-none fixed inset-x-0 top-2 z-50 flex justify-center transition-opacity duration-200',
        offline ? 'opacity-100' : 'opacity-0',
      )}
      aria-hidden={!offline}
    >
      <span className="flex items-center gap-2 rounded-full bg-destructive px-3 py-1.5 text-xs font-medium text-destructive-foreground shadow-md">
        <WifiOff className="size-3.5" aria-hidden />
        Offline — reconnecting…
      </span>
    </div>
  )
}
