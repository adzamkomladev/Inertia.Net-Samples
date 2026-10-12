import { watch } from 'vue'
import { usePage } from '@inertiajs/vue3'
import { toast } from 'vue-sonner'

/** Shows `flash.toast` from the server (Back().WithFlash("toast", ...)) as a sonner toast. */
export function useFlashToast() {
  const page = usePage()
  watch(
    () => page.flash?.toast,
    (t) => {
      if (!t) return
      if (t.type === 'error') toast.error(t.message)
      else if (t.type === 'info') toast.info(t.message)
      else toast.success(t.message)
    },
    { immediate: true },
  )
}
