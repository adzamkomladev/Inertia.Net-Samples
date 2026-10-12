<script lang="ts">
  import { Link, usePage } from '@inertiajs/svelte'
  import { Toaster } from 'svelte-sonner'
  import type { Snippet } from 'svelte'
  import { useFlashToast } from '@/lib/flash-toast.svelte'
  import { useTheme } from '@/lib/theme.svelte'

  let { children }: { children?: Snippet } = $props()

  const browser = typeof window !== 'undefined'
  const page = usePage()
  const appName = $derived((page.props as { appName?: string }).appName ?? 'Inkwell')
  useFlashToast()
  useTheme()
</script>

<div class="bg-background grid min-h-screen lg:grid-cols-2">
  <div class="flex flex-col p-6 sm:p-8">
    <Link
      href="/posts"
      class="focus-visible:ring-ring flex w-fit items-center gap-2 rounded-lg text-sm font-semibold tracking-tight outline-none focus-visible:ring-2"
    >
      <span class="bg-foreground text-background flex size-8 items-center justify-center rounded-lg font-serif text-lg leading-none font-semibold" aria-hidden="true">{appName.charAt(0)}</span>
      {appName}
    </Link>
    <main class="animate-rise mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">
      {@render children?.()}
    </main>
    <p class="text-muted-foreground text-center text-xs">&copy; {new Date().getFullYear()} {appName}</p>
  </div>
  <aside class="bg-muted hidden items-center p-16 lg:flex">
    <blockquote class="max-w-md font-serif text-3xl leading-snug tracking-tight">
      Write the thing you wish you had been able to read.
    </blockquote>
  </aside>
  {#if browser}<Toaster position="bottom-right" closeButton />{/if}
</div>
