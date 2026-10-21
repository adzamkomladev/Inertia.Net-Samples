<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, usePage } from '@inertiajs/svelte'
  import { Toaster } from 'svelte-sonner'
  import BrandMark from '@/components/BrandMark.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useTheme } from '@/lib/theme.svelte'

  /** Centered auth shell with a deck-plated navy panel on the side. */
  let { children }: { children?: Snippet } = $props()

  const browser = typeof window !== 'undefined'
  const page = usePage()
  const appName = $derived((page.props as { appName?: string }).appName ?? 'CarParker')
  useFlashToast()
  useTheme()
</script>

<div class="bg-background grid min-h-screen lg:grid-cols-2">
  <div class="flex flex-col p-6 sm:p-8">
    <BrandMark href="/lots" />
    <main class="animate-rise mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">
      {@render children?.()}
    </main>
    <p class="text-muted-foreground text-center text-xs">&copy; {new Date().getFullYear()} {appName}</p>
  </div>
  <aside class="bg-primary relative hidden items-center overflow-hidden p-16 lg:flex">
    <div class="deck-grid absolute inset-0 opacity-40" aria-hidden="true"></div>
    <div class="relative">
      <p class="font-display text-cta text-sm font-semibold tracking-[0.2em] uppercase">Deck ops, zero circling</p>
      <blockquote class="text-primary-foreground mt-4 max-w-md font-display text-4xl leading-tight font-semibold tracking-tight">
        Reserve the spot before you drive. Roll in, roll out.
      </blockquote>
      <div class="mt-10 grid max-w-md grid-cols-3 gap-4">
        {#each [['Mint tiles', 'Live free spots'], ['Weekly loops', 'Recurring windows'], ['Spot Cover', 'Damage & theft']] as [title, sub] (title)}
          <div class="bg-primary-foreground/10 rounded-2xl p-4">
            <p class="text-cta font-mono text-[13px] font-semibold">{title}</p>
            <p class="text-primary-foreground/70 mt-1 text-xs leading-5">{sub}</p>
          </div>
        {/each}
      </div>
      <p class="text-primary-foreground/50 mt-12 text-xs">
        By continuing you agree to the
        <Link href="/policies" class="underline underline-offset-4 hover:opacity-80">parking policies</Link>.
      </p>
    </div>
  </aside>
  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
