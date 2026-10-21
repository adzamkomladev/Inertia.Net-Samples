<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch, type Component } from 'vue'
import { Link, router, usePage } from '@inertiajs/vue3'
import {
  ArrowLeft, Bell, BarChart3, BellRing, LogOut, Monitor, Moon, Package, PackageCheck, ShieldCheck,
  ShoppingCart, Star, Sun, Truck, UsersRound,
} from '@lucide/vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { applyTheme, useTheme } from '@/composables/useTheme'
import { useRealtime } from '@/composables/useRealtime'
import { cn } from '@/lib/utils'
import type { Theme } from '@/types'

type NavEntry = { href: string; label: string; icon: Component; exact?: boolean }

const page = usePage()
const user = computed(() => page.props.auth.user)
const initials = computed(() => user.value?.name.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase() ?? '')

useTheme()
useRealtime(computed(() => user.value?.id))
useFlashToast()

// Admin defaults to dark: force the dark palette while the console is open, restore on the way out.
const wasDark = ref(true)
onMounted(() => {
  wasDark.value = document.documentElement.classList.contains('dark')
  document.documentElement.classList.add('dark')
})
onUnmounted(() => {
  if (!wasDark.value) document.documentElement.classList.remove('dark')
})

const nav: NavEntry[] = [
  { href: '/admin', label: 'Dashboard', icon: PackageCheck, exact: true },
  { href: '/admin/analytics', label: 'Analytics', icon: BarChart3 },
  { href: '/admin/catalog', label: 'Catalog', icon: Package },
  { href: '/admin/orders', label: 'Orders', icon: ShoppingCart },
  { href: '/admin/deliveries', label: 'Deliveries', icon: Truck },
  { href: '/admin/credit', label: 'BNPL plans', icon: Bell },
  { href: '/admin/care', label: 'Care desk', icon: ShieldCheck },
  { href: '/admin/support', label: 'Complaints', icon: UsersRound },
  { href: '/admin/reviews', label: 'Reviews', icon: Star },
]

const isActive = (entry: NavEntry) => {
  const url = page.url.split('?')[0]!
  if (entry.exact) return url === entry.href
  return url === entry.href || url.startsWith(`${entry.href}/`)
}

const logout = () => {
  router.flushAll()
  router.post('/logout')
}

const mobileNavOpen = ref(false)
watch(() => page.url, () => (mobileNavOpen.value = false))

type ThemeIcon = typeof Sun
const toggles: { theme: Theme; icon: ThemeIcon; label: string }[] = [
  { theme: 'light', icon: Sun, label: 'Light' },
  { theme: 'dark', icon: Moon, label: 'Dark' },
  { theme: 'system', icon: Monitor, label: 'System' },
]
const currentTheme = ref<Theme>('dark')
onMounted(() => {
  const m = document.cookie.match(/(?:^|;\s*)theme=(light|dark|system)/)
  currentTheme.value = (m?.[1] as Theme) ?? 'dark'
})
const setTheme = (t: Theme) => {
  currentTheme.value = t
  applyTheme(t)
  if (t !== 'dark') wasDark.value = true // keep the console dark-friendly on unmount
}
</script>

<template>
  <div class="bg-background flex min-h-screen">
    <aside class="bg-card fixed inset-y-0 left-0 z-30 hidden w-72 flex-col border-r px-4 py-5 lg:flex">
      <div class="flex items-center gap-2.5 px-2 py-1">
        <span class="from-brand to-primary text-brand-foreground flex size-8 items-center justify-center rounded-lg bg-gradient-to-br shadow-md" aria-hidden="true">
          <BarChart3 class="size-4" />
        </span>
        <span class="min-w-0">
          <span class="font-display block text-sm font-bold tracking-tight">{{ page.props.appName }}</span>
          <span class="text-brand text-[11px] font-semibold tracking-[0.14em] uppercase">Admin console</span>
        </span>
      </div>

      <nav aria-label="Admin navigation" class="mt-6 flex flex-col gap-0.5">
        <Link
          v-for="entry in nav" :key="entry.href" :href="entry.href" prefetch
          :aria-current="isActive(entry) ? 'page' : undefined"
          :class="
            cn(
              'group flex items-center gap-3 rounded-xl px-3 py-2 text-sm transition-colors hover:bg-accent hover:text-accent-foreground',
              isActive(entry) && 'bg-accent text-accent-foreground font-medium',
            )
          "
        >
          <component
            :is="entry.icon"
            :class="cn('size-[18px] transition-transform group-active:scale-90', isActive(entry) && 'text-brand')"
            :stroke-width="isActive(entry) ? 2.25 : 1.75"
            aria-hidden="true"
          />
          {{ entry.label }}
        </Link>
      </nav>

      <div class="mt-auto space-y-3">
        <div class="bg-muted/60 flex items-center gap-3 rounded-xl border p-3">
          <Avatar class="size-8">
            <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" alt="" />
            <AvatarFallback class="text-xs">{{ initials }}</AvatarFallback>
          </Avatar>
          <div class="min-w-0">
            <p class="truncate text-sm font-medium">{{ user?.name }}</p>
            <p class="text-muted-foreground truncate text-xs">{{ user?.email }}</p>
          </div>
        </div>
        <Button variant="outline" as-child class="w-full rounded-xl">
          <Link href="/" prefetch><ArrowLeft class="size-4" /> View storefront</Link>
        </Button>
      </div>
    </aside>

    <div class="min-w-0 flex-1 lg:pl-72">
      <header class="glass sticky top-0 z-30 border-b">
        <div class="flex h-14 items-center gap-3 px-4 sm:px-6">
          <Link href="/admin" prefetch class="flex items-center gap-2 lg:hidden">
            <span class="from-brand to-primary text-brand-foreground flex size-7 items-center justify-center rounded-lg bg-gradient-to-br" aria-hidden="true">
              <BarChart3 class="size-3.5" />
            </span>
            <span class="font-display font-bold">Admin</span>
          </Link>
          <Button variant="ghost" size="icon" class="rounded-xl lg:hidden" aria-label="Toggle navigation" @click="mobileNavOpen = !mobileNavOpen">
            <Bell v-if="mobileNavOpen" class="size-5" />
            <Package v-else class="size-5" />
          </Button>

          <div class="ml-auto flex items-center gap-2">
            <!-- Needs-attention bell: jumps to the care desk / queues that usually need a human. -->
            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <Button variant="ghost" size="icon" class="relative rounded-xl" aria-label="Needs attention">
                  <BellRing class="size-4.5" />
                  <span class="bg-destructive absolute -top-0.5 -right-0.5 flex size-2.5 rounded-full ring-2 ring-card" aria-hidden="true" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" class="w-64">
                <DropdownMenuLabel class="font-normal">Needs attention</DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem as-child><Link href="/admin/care?tab=warranty" prefetch><ShieldCheck class="size-4" /> Warranty claims</Link></DropdownMenuItem>
                <DropdownMenuItem as-child><Link href="/admin/care?tab=insurance" prefetch><ShieldCheck class="size-4" /> Insurance claims</Link></DropdownMenuItem>
                <DropdownMenuItem as-child><Link href="/admin/deliveries" prefetch><Truck class="size-4" /> Unassigned deliveries</Link></DropdownMenuItem>
                <DropdownMenuItem as-child><Link href="/admin/support?filter=overdue" prefetch><UsersRound class="size-4" /> Overdue complaints</Link></DropdownMenuItem>
                <DropdownMenuItem as-child><Link href="/admin/catalog" prefetch><Package class="size-4" /> Low stock catalog</Link></DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>

            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <Button variant="ghost" size="icon" class="rounded-xl" aria-label="Change theme">
                  <Sun v-if="currentTheme === 'light'" class="size-4.5" />
                  <Moon v-else-if="currentTheme === 'dark'" class="size-4.5" />
                  <Monitor v-else class="size-4.5" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" class="w-36">
                <DropdownMenuItem v-for="t in toggles" :key="t.theme" @select="setTheme(t.theme)">
                  <component :is="t.icon" class="size-4" /> {{ t.label }}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>

            <DropdownMenu>
              <DropdownMenuTrigger as-child>
                <button type="button" class="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring" aria-label="Account menu">
                  <Avatar class="size-8">
                    <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" alt="" />
                    <AvatarFallback class="bg-primary text-primary-foreground text-xs">{{ initials }}</AvatarFallback>
                  </Avatar>
                </button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" class="w-56">
                <DropdownMenuLabel class="font-normal">
                  <div class="truncate text-sm font-medium">{{ user?.name }}</div>
                  <div class="text-muted-foreground truncate text-xs">{{ user?.email }}</div>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem as-child><Link href="/account" prefetch>My account</Link></DropdownMenuItem>
                <DropdownMenuItem @select="logout"><LogOut class="size-4" /> Log out</DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
        <nav v-if="mobileNavOpen" aria-label="Mobile admin navigation" class="flex flex-col gap-1 border-t p-3 lg:hidden">
          <Link
            v-for="entry in nav" :key="entry.href" :href="entry.href" prefetch
            :class="cn('flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm hover:bg-accent', isActive(entry) && 'bg-accent font-medium')"
          >
            <component :is="entry.icon" class="size-4" aria-hidden="true" />
            {{ entry.label }}
          </Link>
        </nav>
      </header>

      <main class="animate-rise mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
        <slot />
      </main>
    </div>

    <Toaster position="bottom-right" close-button />
  </div>
</template>
