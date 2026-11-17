<script lang="ts">
  import { Toaster } from 'svelte-sonner'

  // The one toaster for the whole app: mounted by app.ts outside Inertia's page tree, so it survives layout changes.
  // On phones toasts sit at the top so they never cover the sticky action bars and bottom navigation.
  let phone = $state(typeof window !== 'undefined' && matchMedia('(max-width: 639px)').matches)
  $effect(() => {
    const query = matchMedia('(max-width: 639px)')
    const update = () => (phone = query.matches)
    update()
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  })
</script>

<Toaster position={phone ? 'top-center' : 'bottom-right'} closeButton />
