<script setup lang="ts">
import { computed, ref, type Component } from 'vue'
import { Link, usePage } from '@inertiajs/vue3'
import { ArrowLeft, Search } from '@lucide/vue'
import BrandMark from '@/components/BrandMark.vue'
import CommandPalette from '@/components/CommandPalette.vue'
import ThemeToggle from '@/components/ThemeToggle.vue'
import UserMenu from '@/components/UserMenu.vue'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useRealtime } from '@/composables/useRealtime'
import { useTheme } from '@/composables/useTheme'
import { cn } from '@/lib/utils'

export type NavItem = { href: string; label: string; icon: Component; exact?: boolean }
export type NavGroup = { label?: string; items: NavItem[] }

const props = defineProps<{
  groups: NavGroup[]
  /** Small label under the wordmark, e.g. "Admin". */
  area: string
  /** Enables the Ctrl/Cmd+K jump-to palette. */
  palette?: boolean
  width?: string
}>()

const page = usePage()
const user = computed(() => page.props.auth.user)

useTheme()
useRealtime(computed(() => user.value?.id))
useFlashToast()

const items = computed(() => props.groups.flatMap((g) => g.items))
const isActive = (item: NavItem) => {
  const url = page.url.split('?')[0]!
  return item.exact ? url === item.href : url === item.href || url.startsWith(`${item.href}/`)
}

const paletteOpen = ref(false)
</script>

<template>
  <div class="bg-background min-h-screen">
    <a href="#main" class="bg-card sr-only z-50 rounded-md px-3 py-2 text-sm focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

    <aside class="bg-card fixed inset-y-0 left-0 z-30 hidden w-64 flex-col border-r lg:flex">
      <div class="flex h-16 items-center gap-2.5 px-6">
        <Link href="/" prefetch aria-label="Back to the store"><BrandMark size="sm" /></Link>
        <span class="text-muted-foreground bg-muted rounded-full px-2 py-0.5 text-xs font-medium">{{ area }}</span>
      </div>

      <nav :aria-label="`${area} navigation`" class="flex-1 space-y-6 overflow-y-auto px-3 py-2">
        <div v-for="(g, gi) in groups" :key="gi" class="space-y-0.5">
          <p v-if="g.label" class="text-muted-foreground px-3 pb-1 text-xs font-medium">{{ g.label }}</p>
          <Link
            v-for="item in g.items" :key="item.href" :href="item.href" prefetch :aria-current="isActive(item) ? 'page' : undefined"
            :class="cn('flex h-10 items-center gap-3 rounded-lg px-3 text-sm transition-colors', isActive(item) ? 'bg-accent text-accent-foreground font-medium' : 'text-muted-foreground hover:bg-muted hover:text-foreground')"
          >
            <component :is="item.icon" class="size-[18px]" :stroke-width="isActive(item) ? 2.1 : 1.75" aria-hidden="true" />
            {{ item.label }}
          </Link>
        </div>
      </nav>

      <div class="space-y-1 border-t p-3">
        <button
          v-if="palette" type="button" class="text-muted-foreground hover:bg-muted flex h-10 w-full items-center gap-3 rounded-lg px-3 text-sm"
          @click="paletteOpen = true"
        >
          <Search class="size-[18px]" aria-hidden="true" /> Jump to…
          <kbd class="bg-muted ml-auto rounded px-1.5 py-0.5 text-[11px]">Ctrl K</kbd>
        </button>
        <Link href="/" prefetch class="text-muted-foreground hover:bg-muted hover:text-foreground flex h-10 items-center gap-3 rounded-lg px-3 text-sm">
          <ArrowLeft class="size-[18px]" aria-hidden="true" /> Back to the store
        </Link>
        <div class="flex items-center gap-1 px-1 pt-1">
          <UserMenu />
          <div class="min-w-0 flex-1 px-1">
            <p class="truncate text-sm font-medium">{{ user?.name }}</p>
            <p class="text-muted-foreground truncate text-xs">{{ user?.email }}</p>
          </div>
          <ThemeToggle />
        </div>
      </div>
    </aside>

    <div class="min-w-0 lg:pl-64">
      <header class="glass sticky top-0 z-30 border-b lg:hidden">
        <div class="flex h-14 items-center gap-2 px-4">
          <Link href="/" prefetch aria-label="Back to the store"><BrandMark size="sm" /></Link>
          <span class="text-muted-foreground bg-muted rounded-full px-2 py-0.5 text-xs font-medium">{{ area }}</span>
          <div class="ml-auto flex items-center">
            <Button v-if="palette" variant="ghost" size="icon" aria-label="Jump to a page" @click="paletteOpen = true"><Search class="size-[18px]" /></Button>
            <ThemeToggle />
            <UserMenu />
          </div>
        </div>
        <nav :aria-label="`${area} navigation`" class="scrollbar-none flex gap-1.5 overflow-x-auto px-4 pb-2.5">
          <Link
            v-for="item in items" :key="item.href" :href="item.href" prefetch :aria-current="isActive(item) ? 'page' : undefined"
            :class="cn('flex h-10 shrink-0 items-center gap-2 rounded-full px-4 text-sm whitespace-nowrap transition-colors', isActive(item) ? 'bg-foreground text-background font-medium' : 'bg-muted text-muted-foreground')"
          >
            {{ item.label }}
          </Link>
        </nav>
      </header>

      <main id="main" :class="cn('mx-auto w-full px-4 py-6 pb-20 sm:px-6 lg:px-10 lg:py-10', width ?? 'max-w-6xl')">
        <slot />
      </main>
    </div>

    <CommandPalette v-if="palette" v-model:open="paletteOpen" :items="items" />
    <Toaster position="bottom-right" close-button />
  </div>
</template>
