import { useEffect } from 'react'
import { xsrfToken } from '@/lib/upload'

/**
 * Records a real visit after mount so cached/prefetched navigations still count.
 * Analytics failures never interrupt reading.
 */
export function useViewBeacon(postId: string, enabled = true) {
  useEffect(() => {
    if (!enabled) return
    void fetch(`/posts/${postId}/view`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-XSRF-TOKEN': xsrfToken() },
    }).catch(() => {})
  }, [postId, enabled])
}
