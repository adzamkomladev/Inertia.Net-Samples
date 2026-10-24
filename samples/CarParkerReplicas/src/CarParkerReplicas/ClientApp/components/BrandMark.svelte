<script lang="ts">
  import { Link, usePage } from '@inertiajs/svelte'
  import { cn } from '@/lib/utils'

  /** CarParker wordmark: a petrol bay-sign tile with a "P" and a condensed wordmark. */
  let {
    href = '/',
    size = 'md',
    class: className,
  }: {
    href?: string
    size?: 'sm' | 'md'
    class?: string
  } = $props()

  const page = usePage()
  const appName = $derived((page.props as { appName?: string }).appName ?? 'CarParker')
</script>

<Link {href} class={cn('flex w-fit items-center gap-2.5 rounded-md', className)} aria-label={`${appName} home`}>
  <span
    class={cn(
      'bg-primary text-primary-foreground font-display flex items-center justify-center rounded-lg leading-none font-bold',
      size === 'md' ? 'size-9 text-xl' : 'size-8 text-lg',
    )}
    aria-hidden="true"
  >P</span>
  <span class={cn('font-display font-bold tracking-tight', size === 'md' ? 'text-xl' : 'text-lg')}>{appName}</span>
</Link>
