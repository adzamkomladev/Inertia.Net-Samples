import { useEffect } from 'react'
import { toast } from 'sonner'
import type { Flash } from '@/types/shared'

/** Renders server flash messages (Back().WithFlash(...)) as sonner toasts. */
export function useFlashToasts(flash: Flash | undefined) {
  useEffect(() => {
    if (flash?.success) toast.success(flash.success)
    if (flash?.error) toast.error(flash.error)
    if (flash?.info) toast.info(flash.info)
  }, [flash])
}
