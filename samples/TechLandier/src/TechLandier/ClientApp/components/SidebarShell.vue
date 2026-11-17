<script setup lang="ts">
import { computed, ref, watch, type Component } from 'vue'
import { Link, usePage } from '@inertiajs/vue3'
import { ArrowLeft, Ellipsis, Menu, Search } from '@lucide/vue'
import BrandMark from '@/components/BrandMark.vue'
import CommandPalette from '@/components/CommandPalette.vue'
import NotificationBell from '@/components/NotificationBell.vue'
import NotificationSound from '@/components/NotificationSound.vue'
import ThemeToggle from '@/components/ThemeToggle.vue'
import UserMenu from '@/components/UserMenu.vue'
import { Button } from '@/components/ui/button'
import NavigationSkeleton from '@/components/NavigationSkeleton.vue'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { usePendingVisit } from '@/composables/usePendingVisit'
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
  /** Hrefs of the four destinations pinned to the phone bottom bar; the rest live behind More. */
  quick?: string[]
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
const drawerOpen = ref(false)
watch(() => page.url, () => (drawerOpen.value = false))

const pending = usePendingVisit()
const quickItems = computed(() => {
  const picked = (props.quick ?? []).map((href) => items.value.find((i) => i.href === href)).filter((i): i is NavItem => !!i)
  return picked.length ? picked : items.value.slice(0, 4)
})
const moreActive = computed(() => !quickItems.value.some(isActive))
</script>

<template>
  <div class="bg-background min-h-screen">
    <a href="#main" class="bg-card sr-only z-50 rounded-md px-3 py-2 text-sm focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

    <aside class="bg-card fixed inset-y-0 left-0 z-30 hidden w-64 flex-col border-r lg:flex">
      <div class="flex h-16 items-center gap-2.5 px-6">
        <Link href="/" prefetch cache-for="30s" aria-label="Back to the store"><BrandMark size="sm" /></Link>
        <span class="text-muted-foreground bg-muted hidden rounded-full px-2 py-0.5 text-xs font-medium sm:inline">{{ area }}</span>
      </div>

      <nav :aria-label="`${area} navigation`" class="flex-1 space-y-6 overflow-y-auto px-3 py-2">
        <div v-for="(g, gi) in groups" :key="gi" class="space-y-0.5">
          <p v-if="g.label" class="text-muted-foreground px-3 pb-1 text-xs font-medium">{{ g.label }}</p>
          <Link
            v-for="item in g.items" :key="item.href" :href="item.href" prefetch cache-for="30s" :aria-current="isActive(item) ? 'page' : undefined"
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
        <Link href="/" prefetch cache-for="30s" class="text-muted-foreground hover:bg-muted hover:text-foreground flex h-10 items-center gap-3 rounded-lg px-3 text-sm">
          <ArrowLeft class="size-[18px]" aria-hidden="true" /> Back to the store
        </Link>
        <div class="flex items-center gap-1 px-1 pt-1">
          <UserMenu />
          <div class="min-w-0 flex-1 px-1">
            <p class="truncate text-sm font-medium">{{ user?.name }}</p>
            <p class="text-muted-foreground truncate text-xs">{{ user?.email }}</p>
          </div>
          <NotificationBell /><NotificationSound /><ThemeToggle />
        </div>
      </div>
    </aside>

    <div class="min-w-0 lg:pl-64">
      <header class="glass sticky top-0 z-30 border-b pt-[env(safe-area-inset-top)] lg:hidden">
        <div class="flex h-14 items-center gap-1 px-2 sm:px-4">
          <Button variant="ghost" size="icon" aria-label="Open navigation" aria-haspopup="dialog" :aria-expanded="drawerOpen" @click="drawerOpen = true"><Menu class="size-5" /></Button>
          <Link href="/" prefetch cache-for="30s" aria-label="Back to the store"><BrandMark size="sm" /></Link>
          <span class="text-muted-foreground bg-muted hidden rounded-full px-2 py-0.5 text-xs font-medium sm:inline">{{ area }}</span>
          <div class="ml-auto flex items-center">
            <Button v-if="palette" variant="ghost" size="icon" aria-label="Jump to a page" @click="paletteOpen = true"><Search class="size-[18px]" /></Button>
            <NotificationBell />
            <span class="hidden items-center sm:flex"><NotificationSound /><ThemeToggle /></span>
            <UserMenu />
          </div>
        </div>
      </header>

      <main id="main" :class="cn('mx-auto w-full px-4 py-6 pb-[calc(5.5rem+env(safe-area-inset-bottom))] sm:px-6 lg:px-10 lg:py-10 lg:pb-20', width ?? 'max-w-6xl')">
        <NavigationSkeleton v-if="pending" :kind="pending.kind" />
        <div v-show="!pending" class="contents"><slot /></div>
      </main>
    </div>

    <!-- Phones: four pinned destinations plus More, always visible (hidden navigation is slower to use). -->
    <nav :aria-label="`${area} quick navigation`" class="glass fixed inset-x-0 bottom-0 z-30 border-t pb-[env(safe-area-inset-bottom)] lg:hidden">
      <ul class="mx-auto grid max-w-lg grid-cols-5">
        <li v-for="item in quickItems" :key="item.href">
          <Link
            :href="item.href" prefetch cache-for="30s" :aria-current="isActive(item) ? 'page' : undefined"
            :class="cn('flex min-h-14 flex-col items-center justify-center gap-1 px-1 text-[11px] leading-none transition-colors', isActive(item) ? 'text-primary font-medium' : 'text-muted-foreground')"
          >
            <component :is="item.icon" class="size-5" :stroke-width="isActive(item) ? 2.2 : 1.75" aria-hidden="true" />
            <span class="max-w-full truncate">{{ item.label }}</span>
          </Link>
        </li>
        <li>
          <button
            type="button" aria-haspopup="dialog" :aria-expanded="drawerOpen"
            :class="cn('flex min-h-14 w-full flex-col items-center justify-center gap-1 px-1 text-[11px] leading-none', moreActive ? 'text-primary font-medium' : 'text-muted-foreground')"
            @click="drawerOpen = true"
          >
            <Ellipsis class="size-5" aria-hidden="true" /> More
          </button>
        </li>
      </ul>
    </nav>

    <Sheet v-model:open="drawerOpen">
      <SheetContent side="left" class="w-[85vw] max-w-sm gap-0 p-0 pb-[env(safe-area-inset-bottom)] lg:hidden">
        <SheetHeader class="flex-row items-center gap-2.5 border-b px-4 py-3 pt-[max(0.75rem,env(safe-area-inset-top))]">
          <SheetTitle class="sr-only">{{ area }} navigation</SheetTitle>
          <SheetDescription class="sr-only">Every page in the {{ area.toLowerCase() }} area.</SheetDescription>
          <BrandMark size="sm" />
          <span class="text-muted-foreground bg-muted rounded-full px-2 py-0.5 text-xs font-medium">{{ area }}</span>
        </SheetHeader>
        <nav :aria-label="`${area} pages`" class="flex-1 space-y-5 overflow-y-auto px-3 py-3">
          <div v-for="(g, gi) in groups" :key="gi" class="space-y-0.5">
            <p v-if="g.label" class="text-muted-foreground px-3 pb-1 text-xs font-medium">{{ g.label }}</p>
            <Link
              v-for="item in g.items" :key="item.href" :href="item.href" prefetch cache-for="30s" :aria-current="isActive(item) ? 'page' : undefined"
              :class="cn('flex min-h-11 items-center gap-3 rounded-lg px-3 text-[15px] transition-colors', isActive(item) ? 'bg-accent text-accent-foreground font-medium' : 'text-muted-foreground active:bg-muted')"
            >
              <component :is="item.icon" class="size-5" :stroke-width="isActive(item) ? 2.1 : 1.75" aria-hidden="true" />
              {{ item.label }}
            </Link>
          </div>
        </nav>
        <div class="space-y-1 border-t p-3">
          <Link href="/" prefetch cache-for="30s" class="text-muted-foreground flex min-h-11 items-center gap-3 rounded-lg px-3 text-[15px] active:bg-muted">
            <ArrowLeft class="size-5" aria-hidden="true" /> Back to the store
          </Link>
          <div class="flex items-center gap-2 px-1">
            <div class="min-w-0 flex-1 px-2">
              <p class="truncate text-sm font-medium">{{ user?.name }}</p>
              <p class="text-muted-foreground truncate text-xs">{{ user?.email }}</p>
            </div>
            <NotificationSound /><ThemeToggle />
          </div>
        </div>
      </SheetContent>
    </Sheet>
    <CommandPalette v-if="palette" v-model:open="paletteOpen" :items="items" />
  </div>
</template>
