<script lang="ts">
  import { usePage } from '@inertiajs/svelte'
  import { cn } from '@/lib/utils'

  /** CarParker wordmark: amber "P" deck tile + Space Grotesk wordmark. */
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

<a href={href} data-inertia-link class={cn('group flex w-fit items-center gap-2.5', className)}>
  <span
    class="bg-cta text-cta-foreground shadow-xs flex items-center justify-center rounded-xl font-display leading-none font-bold transition-transform duration-300 ease-spring group-hover:-rotate-6",
    class:size-8={size === 'md'}
    class:size-7={size === 'sm'}
    class:text-lg={size === 'md'}
    class:text-base={size === 'sm'}
    aria-hidden="true"
  >P</span>
  <span class="font-display tracking-tight" class:text-[17px]={size === 'md'} class:text-base={size === 'sm'} class:font-semibold={true}>{appName}</span>
</a>
