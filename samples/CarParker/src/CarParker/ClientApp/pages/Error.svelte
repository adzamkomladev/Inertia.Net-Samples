<script lang="ts">
  import CircleOff from '@lucide/svelte/icons/circle-off'
  import Compass from '@lucide/svelte/icons/compass'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'

  /** Shared 4xx/5xx page; the backend renders it with a `status` prop. */
  let { status }: { status: number } = $props()

  const messages: Record<number, { title: string; description: string }> = {
    401: { title: 'Sign in to continue', description: 'This page needs an account. Log in, then you will land back here.' },
    403: { title: 'No access', description: "Your account can't open this console or page. If you think it should, ask an admin to check your role." },
    404: { title: 'Bay not found', description: "This page or lot doesn't exist, or it was removed. Check the address, or search for a lot." },
    419: { title: 'Session expired', description: 'Your session timed out, and nothing was saved. Refresh the page and try again.' },
    429: { title: 'Too many requests', description: 'Slow down a little and try again in a moment.' },
    500: { title: 'Deck system fault', description: 'Something broke on our side, not yours. Try again in a moment; your bookings are safe.' },
    503: { title: 'Gate closed', description: "We're down for maintenance. Please check back soon." },
  }
  const error = $derived(messages[status] ?? { title: 'Unexpected error', description: 'An unexpected error occurred. Please try again.' })
  const goBack = () => window.history.back()
</script>

<PageTitle title={error.title} />
<div class="mx-auto flex max-w-md flex-col items-center px-4 py-20 text-center" role="alert">
  <div class="bg-card shadow-card flex size-20 items-center justify-center rounded-xl">
    <CircleOff class="text-muted-foreground size-8" strokeWidth={1.5} aria-hidden="true" />
  </div>
  <p class="text-muted-foreground/50 font-display mt-6 text-6xl font-semibold tracking-tight tabular-nums">{status}</p>
  <h1 class="font-display mt-2 text-2xl font-semibold tracking-tight">{error.title}</h1>
  <p class="text-muted-foreground mt-2 text-sm leading-6">{error.description}</p>
  <div class="mt-8 flex flex-wrap justify-center gap-2">
    <Button href="/lots" prefetch="hover" cacheFor="30s">
      <Compass class="size-4" aria-hidden="true" /> Find parking
    </Button>
    <Button variant="outline"  onclick={goBack}>Go back</Button>
  </div>
</div>
