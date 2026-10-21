import { usePage } from '@inertiajs/svelte'
import { toast } from 'svelte-sonner'
import type { Toast } from '@/types'

/** Shows `flash.toast` from the server (Back().WithFlash("toast", ...)) as a sonner toast. */
export function useFlashToast() {
  const page = usePage()
  $effect(() => {
    const current = (page.flash as { toast?: Toast } | undefined)?.toast
    if (!current) return
    if (current.type === 'error') toast.error(current.message)
    else if (current.type === 'info') toast.info(current.message)
    else toast.success(current.message)
  })
}
