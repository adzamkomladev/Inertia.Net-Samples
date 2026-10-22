<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Link, usePage } from '@inertiajs/svelte'
  import { Toaster } from 'svelte-sonner'
  import BrandMark from '@/components/BrandMark.svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useTheme } from '@/lib/theme.svelte'

  /** Centered auth shell; the side panel is a row of painted bays with the three steps of a booking. */
  let { children }: { children?: Snippet } = $props()

  const browser = typeof window !== 'undefined'
  const page = usePage()
  const appName = $derived((page.props as { appName?: string }).appName ?? 'CarParker')
  useFlashToast()
  useTheme()

  const steps = ['Pick a numbered bay on the lot map.', 'Choose the hours, or a weekly repeat.', 'Show your plate at the gate and drive in.']
</script>

<div class="bg-background grid min-h-screen lg:grid-cols-2">
  <div class="flex flex-col p-6 sm:p-8">
    <BrandMark href="/lots" />
    <main class="mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">
      {@render children?.()}
    </main>
    <p class="text-muted-foreground text-center text-xs">&copy; {new Date().getFullYear()} {appName}</p>
  </div>
  <aside class="bg-primary text-primary-foreground relative hidden flex-col justify-end overflow-hidden p-16 lg:flex">
    <!-- Painted bays -->
    <div class="absolute inset-x-0 top-0 flex h-56 items-stretch gap-0 px-8 pt-0" aria-hidden="true">
      {#each Array.from({ length: 9 }) as _, i (i)}
        <div class="border-primary-foreground/30 flex flex-1 items-end justify-center border-x pb-4">
          <span class="font-display text-primary-foreground/50 text-2xl font-bold tabular-nums">{String(i + 1).padStart(2, '0')}</span>
        </div>
      {/each}
    </div>
    <ol class="relative grid max-w-md gap-4">
      {#each steps as step, i (step)}
        <li class="flex items-start gap-4">
          <span class="bg-primary-foreground text-primary font-display grid size-8 shrink-0 place-items-center rounded-full text-base font-bold">{i + 1}</span>
          <p class="pt-0.5 text-lg leading-snug">{step}</p>
        </li>
      {/each}
    </ol>
    <p class="text-primary-foreground/70 relative mt-10 text-sm">
      By continuing you agree to the
      <Link href="/policies" class="underline underline-offset-4 hover:opacity-80">parking policies</Link>.
    </p>
  </aside>
  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
