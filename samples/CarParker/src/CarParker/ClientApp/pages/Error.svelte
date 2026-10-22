<script lang="ts">
  import { CircleOff, Compass } from '@lucide/svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'

  /** Shared 4xx/5xx page; the backend renders it with a `status` prop. */
  let { status }: { status: number } = $props()

  const messages: Record<number, { title: string; description: string }> = {
    403: { title: 'No access', description: "You don't have permission to use this console or page." },
    404: { title: 'Bay not found', description: "The page or spot you're looking for doesn't exist or was removed." },
    419: { title: 'Session expired', description: 'Your session timed out. Refresh the page and try again.' },
    429: { title: 'Too many requests', description: 'Slow down a little and try again in a moment.' },
    500: { title: 'Deck system fault', description: 'An unexpected error occurred. The ops team has been pinged.' },
    503: { title: 'Gate closed', description: "We're down for maintenance. Please check back soon." },
  }
  const error = $derived(messages[status] ?? { title: 'Unexpected error', description: 'An unexpected error occurred. Please try again.' })
  const goBack = () => window.history.back()
</script>

<PageTitle title={error.title} />
<div class="mx-auto flex max-w-md flex-col items-center py-20 text-center">
  <div class="bg-card shadow-card flex size-20 items-center justify-center rounded-xl">
    <CircleOff class="text-muted-foreground size-8" strokeWidth={1.5} aria-hidden="true" />
  </div>
  <p class="text-muted-foreground/50 font-display mt-6 text-6xl font-semibold tracking-tight tabular-nums">{status}</p>
  <h1 class="font-display mt-2 text-2xl font-semibold tracking-tight">{error.title}</h1>
  <p class="text-muted-foreground mt-2 text-sm leading-6">{error.description}</p>
  <div class="mt-8 flex gap-2">
    <Button href="/lots" >
      <Compass class="size-4" aria-hidden="true" /> Find parking
    </Button>
    <Button variant="outline"  onclick={goBack}>Go back</Button>
  </div>
</div>
