<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'
  import { buttonVariants, type ButtonSize, type ButtonVariant } from './variants'
  import { cn } from '@/lib/utils'

  type Props = {
    variant?: ButtonVariant
    size?: ButtonSize
    class?: string
    href?: string
    prefetch?: boolean | 'hover' | 'mount'
    cacheFor?: string | number
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    children?: Snippet
    [key: string]: any
  }

  let { variant = 'default', size = 'default', class: className, href, prefetch, cacheFor, type = 'button', disabled, children, ...rest }: Props = $props()
  const classes = $derived(cn(buttonVariants({ variant, size }), className))
</script>

{#if href !== undefined && disabled}
  <!-- A disabled link is not a link: same look, no navigation, announced as disabled. -->
  <span role="link" aria-disabled="true" class={cn(classes, 'pointer-events-none opacity-50')} {...rest}>{@render children?.()}</span>
{:else if href !== undefined}
  <Link {href} {prefetch} {cacheFor} class={classes} {...rest}>{@render children?.()}</Link>
{:else}
  <button {type} {disabled} class={classes} {...rest}>{@render children?.()}</button>
{/if}
