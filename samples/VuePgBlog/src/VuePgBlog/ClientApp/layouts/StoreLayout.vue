<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Link, router, usePage } from '@inertiajs/vue3'
import {
  Check, ChevronDown, LayoutDashboard, LogOut, Menu, Monitor, Moon, Package, Search,
  ShieldCheck, ShoppingBag, Sparkles, Sun, UserRound, X, Zap,
} from '@lucide/vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useTheme, applyTheme } from '@/composables/useTheme'
import { useRealtime } from '@/composables/useRealtime'
import { categoryIcon } from '@/lib/icons'
import type { CategoryTile, Theme } from '@/types'

const page = usePage()
const user = computed(() => page.props.auth.user)
const isAdmin = computed(() => user.value?.role === 'admin')
const initials = computed(() => user.value?.name.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase() ?? '')

useTheme()
useRealtime(computed(() => user.value?.id))
useFlashToast()

watch(() => `${user.value?.id}:${user.value?.verified}`, () => router.flushAll())

const logout = () => {
  router.flushAll()
  router.post('/logout')
}

// ---------------------------------------------------------------- search
const q = ref((page.props.filters as { q?: string } | undefined)?.q ?? '')
const searchFocused = ref(false)
watch(() => (page.props.filters as { q?: string } | undefined)?.q ?? '', (v) => {
  if (!searchFocused.value) q.value = v
})
const submitSearch = () => {
  const term = q.value.trim()
  router.get('/products', term ? { q: term } : {}, { replace: window.location.pathname === '/products' })
  searchFocused.value = false
}

// ---------------------------------------------------------- categories
// Category tiles ship with the storefront index pages; other pages lazily pull
// them from the /products Inertia endpoint the first time the menu opens.
const categories = ref<CategoryTile[]>((page.props.categories as CategoryTile[] | undefined) ?? [])
watch(
  () => page.props.categories as CategoryTile[] | undefined,
  (c) => {
    if (c?.length) categories.value = c
  },
)
const categoriesOpen = ref(false)
const loadCategories = async () => {
  if (categories.value.length) return
  try {
    const res = await fetch('/products', {
      headers: { 'X-Inertia': 'true', 'X-Inertia-Version': page.version, Accept: 'application/json' },
    })
    if (!res.ok) return
    const data = await res.json()
    categories.value = data?.props?.categories ?? []
  } catch {
    /* menu simply stays link-only */
  }
}
watch(categoriesOpen, (open) => {
  if (open) void loadCategories()
})

// ---------------------------------------------------------------- theme
type ThemeIcon = typeof Sun
const toggles: { theme: Theme; icon: ThemeIcon; label: string }[] = [
  { theme: 'light', icon: Sun, label: 'Light' },
  { theme: 'dark', icon: Moon, label: 'Dark' },
  { theme: 'system', icon: Monitor, label: 'System' },
]
const currentTheme = ref<Theme>('system')
if (typeof document !== 'undefined') {
  const m = document.cookie.match(/(?:^|;\s*)theme=(light|dark|system)/)
  currentTheme.value = (m?.[1] as Theme) ?? 'system'
}
const setTheme = (t: Theme) => {
  currentTheme.value = t
  applyTheme(t)
}

const isActive = (href: string) => {
  const path = href.split('?')[0]!
  return window.location.pathname === path || window.location.pathname.startsWith(`${path}/`)
}

const mobileOpen = ref(false)
watch(() => page.url, () => (mobileOpen.value = false))
</script>

<template>
  <div class="bg-background flex min-h-screen flex-col">
    <!-- Announcement strip -->
    <div class="hero-gradient text-brand-foreground relative overflow-hidden px-4 py-1.5 text-center text-xs font-medium sm:text-sm">
      <span class="relative z-10 inline-flex items-center gap-1.5">
        <Zap class="size-3.5" aria-hidden="true" />
        Split any order into 3, 6 or 12 installments — 0% down, instant approval
      </span>
      <div class="animate-pulse-glow absolute inset-0 bg-gradient-to-r from-transparent via-white/10 to-transparent" aria-hidden="true" />
    </div>

    <!-- Sticky glass header -->
    <header class="glass sticky top-0 z-40 border-b">
      <div class="mx-auto flex h-16 max-w-7xl items-center gap-3 px-4 sm:px-6">
        <Link href="/" prefetch class="flex shrink-0 items-center gap-2.5" aria-label="TechLandier home">
          <span class="from-brand to-primary text-brand-foreground flex size-9 items-center justify-center rounded-xl bg-gradient-to-br shadow-md" aria-hidden="true">
            <Zap class="size-4.5" />
          </span>
          <span class="font-display hidden text-lg font-bold tracking-tight sm:block">TechLandier</span>
        </Link>

        <!-- Category dropdown -->
        <DropdownMenu v-model:open="categoriesOpen">
          <DropdownMenuTrigger as-child>
            <Button variant="ghost" class="hidden rounded-xl md:inline-flex">
              <Menu class="size-4" /> Categories <ChevronDown class="size-3.5 opacity-60" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" class="w-72">
            <DropdownMenuItem as-child>
              <Link href="/products" prefetch class="gap-3"><Package class="text-brand size-4" /> All products</Link>
            </DropdownMenuItem>
            <DropdownMenuSeparator v-if="categories.length" />
            <DropdownMenuItem v-for="c in categories" :key="c.id" as-child>
              <Link :href="`/products?category=${c.slug}`" prefetch class="gap-3">
                <span class="bg-accent flex size-7 items-center justify-center rounded-lg" aria-hidden="true">
                  <component :is="categoryIcon(c.icon)" class="size-3.5" />
                </span>
                <span class="min-w-0">
                  <span class="block truncate text-sm">{{ c.name }}</span>
                  <span class="text-muted-foreground block truncate text-xs">{{ c.tagline }}</span>
                </span>
              </Link>
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>

        <!-- Search -->
        <form class="relative mx-auto hidden w-full max-w-md md:block" role="search" @submit.prevent="submitSearch">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="q" type="search" placeholder="Search gadgets, brands…" class="rounded-xl pl-9" aria-label="Search products" @focus="searchFocused = true" @blur="searchFocused = false" />
        </form>

        <nav class="ml-auto hidden items-center gap-1 md:flex" aria-label="Storefront">
          <Button variant="ghost" as-child class="rounded-xl" :class="isActive('/deals') && 'bg-accent text-accent-foreground'">
            <Link href="/deals" prefetch><Sparkles class="size-4" /> Deals</Link>
          </Button>
          <Button variant="ghost" as-child class="rounded-xl" :class="isActive('/support/policy') && 'bg-accent text-accent-foreground'">
            <Link href="/support/policy" prefetch><ShieldCheck class="size-4" /> Support</Link>
          </Button>
        </nav>

        <!-- Theme switcher -->
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button variant="ghost" size="icon" class="rounded-xl" aria-label="Change theme">
              <Sun v-if="currentTheme === 'light'" class="size-4.5" />
              <Moon v-else-if="currentTheme === 'dark'" class="size-4.5" />
              <Monitor v-else class="size-4.5" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-36">
            <DropdownMenuItem v-for="t in toggles" :key="t.theme" :data-checked="currentTheme === t.theme" @select="setTheme(t.theme)">
              <component :is="t.icon" class="size-4" /> {{ t.label }}
              <Check v-if="currentTheme === t.theme" class="ml-auto size-3.5" />
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>

        <!-- Account menu -->
        <DropdownMenu v-if="user">
          <DropdownMenuTrigger as-child>
            <button type="button" class="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring" aria-label="Account menu">
              <Avatar class="size-8">
                <AvatarImage v-if="user.avatarUrl" :src="user.avatarUrl" alt="" />
                <AvatarFallback class="bg-primary text-primary-foreground text-xs">{{ initials }}</AvatarFallback>
              </Avatar>
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-60">
            <DropdownMenuLabel class="font-normal">
              <div class="truncate text-sm font-medium">{{ user.name }}</div>
              <div class="text-muted-foreground truncate text-xs">{{ user.email }}</div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem as-child><Link href="/account" prefetch><UserRound class="size-4" /> My account</Link></DropdownMenuItem>
            <DropdownMenuItem as-child><Link href="/account/orders" prefetch><ShoppingBag class="size-4" /> Orders</Link></DropdownMenuItem>
            <DropdownMenuItem v-if="isAdmin" as-child><Link href="/admin" prefetch><LayoutDashboard class="size-4" /> Admin console</Link></DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem @select="logout"><LogOut class="size-4" /> Log out</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <div v-else class="flex items-center gap-2">
          <Button variant="ghost" as-child class="rounded-xl"><Link href="/login" prefetch>Log in</Link></Button>
          <Button as-child class="glow rounded-xl"><Link href="/register" prefetch>Sign up</Link></Button>
        </div>

        <Button variant="ghost" size="icon" class="rounded-xl md:hidden" aria-label="Open menu" @click="mobileOpen = !mobileOpen">
          <X v-if="mobileOpen" class="size-5" />
          <Menu v-else class="size-5" />
        </Button>
      </div>

      <!-- Mobile panel -->
      <div v-if="mobileOpen" class="border-t md:hidden">
        <form class="relative p-4 pb-2" role="search" @submit.prevent="submitSearch">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-7 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="q" type="search" placeholder="Search products…" class="pl-9" aria-label="Search products" />
        </form>
        <nav class="flex flex-col px-4 pb-4" aria-label="Mobile navigation">
          <Link href="/products" prefetch class="rounded-xl px-3 py-2.5 text-sm font-medium hover:bg-accent">All products</Link>
          <Link v-for="c in categories" :key="c.id" :href="`/products?category=${c.slug}`" prefetch class="text-muted-foreground rounded-xl px-3 py-2 text-sm hover:bg-accent">{{ c.name }}</Link>
          <Link href="/deals" prefetch class="rounded-xl px-3 py-2.5 text-sm font-medium hover:bg-accent">Deals</Link>
          <Link href="/about" prefetch class="rounded-xl px-3 py-2.5 text-sm font-medium hover:bg-accent">About</Link>
          <Link href="/support/policy" prefetch class="rounded-xl px-3 py-2.5 text-sm font-medium hover:bg-accent">Support &amp; policy</Link>
        </nav>
      </div>
    </header>

    <main class="flex-1">
      <slot />
    </main>

    <footer class="border-t">
      <div class="mx-auto flex max-w-7xl flex-col items-center justify-between gap-4 px-4 py-10 sm:flex-row sm:px-6">
        <div class="flex items-center gap-2.5">
          <span class="from-brand to-primary text-brand-foreground flex size-7 items-center justify-center rounded-lg bg-gradient-to-br" aria-hidden="true">
            <Zap class="size-3.5" />
          </span>
          <span class="font-display font-semibold">TechLandier</span>
        </div>
        <nav class="text-muted-foreground flex flex-wrap items-center justify-center gap-x-6 gap-y-2 text-sm" aria-label="Footer">
          <Link href="/products" prefetch class="hover:text-foreground">Shop</Link>
          <Link href="/deals" prefetch class="hover:text-foreground">Deals</Link>
          <Link href="/about" prefetch class="hover:text-foreground">About</Link>
          <Link href="/support/policy" prefetch class="hover:text-foreground">Support &amp; policy</Link>
          <Link href="/account" prefetch class="hover:text-foreground">My account</Link>
        </nav>
        <p class="text-muted-foreground text-xs">&copy; {{ new Date().getFullYear() }} TechLandier. All rights reserved.</p>
      </div>
    </footer>

    <Toaster position="bottom-right" close-button />
  </div>
</template>
