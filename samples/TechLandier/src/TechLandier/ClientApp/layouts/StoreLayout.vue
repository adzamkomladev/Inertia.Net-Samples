<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Link, router, usePage } from '@inertiajs/vue3'
import { ChevronDown, Menu, Search, X } from '@lucide/vue'
import BrandMark from '@/components/BrandMark.vue'
import NotificationSound from '@/components/NotificationSound.vue'
import ThemeToggle from '@/components/ThemeToggle.vue'
import UserMenu from '@/components/UserMenu.vue'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useRealtime } from '@/composables/useRealtime'
import { useTheme } from '@/composables/useTheme'
import type { CategoryTile } from '@/types'

const page = usePage()
const user = computed(() => page.props.auth.user)

useTheme()
useRealtime(computed(() => user.value?.id))
useFlashToast()

watch(() => `${user.value?.id}:${user.value?.verified}`, () => router.flushAll())

// ---------------------------------------------------------------- search
const filterQ = () => (page.props.filters as { q?: string } | undefined)?.q ?? ''
const q = ref(filterQ())
const searchFocused = ref(false)
watch(filterQ, (v) => {
  if (!searchFocused.value) q.value = v
})
const submitSearch = () => {
  const term = q.value.trim()
  router.get('/products', term ? { q: term } : {}, { replace: window.location.pathname === '/products' })
  mobileOpen.value = false
}

// ---------------------------------------------------------- categories
// Category tiles ship with the storefront index pages; other pages pull them
// from the /products Inertia endpoint the first time the menu opens.
const loadedCategories = ref<CategoryTile[]>([])
const categories = computed(() => (page.props.categories as CategoryTile[] | undefined) ?? loadedCategories.value)
const categoriesOpen = ref(false)
const loadCategories = async () => {
  if (categories.value.length) return
  try {
    const res = await fetch('/products', {
      headers: { 'X-Inertia': 'true', 'X-Inertia-Version': String(page.version ?? ''), Accept: 'application/json' },
    })
    if (!res.ok) return
    loadedCategories.value = (await res.json())?.props?.categories ?? []
  } catch {
    /* menu stays link-only */
  }
}
const onCategoriesOpen = (open: boolean) => {
  if (open) void loadCategories()
}

const isActive = (href: string) => page.url === href || page.url.startsWith(`${href}/`) || page.url.startsWith(`${href}?`)

const mobileOpen = ref(false)
watch(() => page.url, () => (mobileOpen.value = false))

const shop = [
  { href: '/products', label: 'All products' },
  { href: '/deals', label: 'Deals' },
]
const help = [
  { href: '/support/policy', label: 'Delivery, warranty & payments' },
  { href: '/about', label: 'About' },
]
</script>

<template>
  <div class="bg-background flex min-h-screen flex-col">
    <a href="#main" class="bg-card sr-only z-50 rounded-md px-3 py-2 text-sm focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>

    <header class="glass sticky top-0 z-40 border-b">
      <div class="mx-auto flex h-16 max-w-7xl items-center gap-2 px-4 sm:gap-4 sm:px-6 lg:px-8">
        <Button variant="ghost" size="icon" class="-ml-2 md:hidden" :aria-label="mobileOpen ? 'Close menu' : 'Open menu'" :aria-expanded="mobileOpen" @click="mobileOpen = !mobileOpen">
          <X v-if="mobileOpen" class="size-5" />
          <Menu v-else class="size-5" />
        </Button>

        <Link href="/" prefetch class="shrink-0" aria-label="TechLandier home"><BrandMark /></Link>

        <nav class="hidden items-center gap-1 md:flex" aria-label="Storefront">
          <DropdownMenu v-model:open="categoriesOpen" @update:open="onCategoriesOpen">
            <DropdownMenuTrigger as-child>
              <Button variant="ghost" size="sm">Shop <ChevronDown class="size-3.5 opacity-60" /></Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" class="w-64">
              <DropdownMenuItem as-child><Link href="/products" prefetch>All products</Link></DropdownMenuItem>
              <DropdownMenuSeparator v-if="categories.length" />
              <DropdownMenuItem v-for="c in categories" :key="c.id" as-child>
                <Link :href="`/products?category=${c.slug}`" prefetch class="justify-between">
                  {{ c.name }} <span class="text-muted-foreground price text-xs">{{ c.count }}</span>
                </Link>
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
          <Button variant="ghost" size="sm" as-child :class="isActive('/deals') && 'bg-muted'">
            <Link href="/deals" prefetch>Deals</Link>
          </Button>
          <Button variant="ghost" size="sm" as-child :class="isActive('/support') && 'bg-muted'">
            <Link href="/support/policy" prefetch>Help</Link>
          </Button>
        </nav>

        <form class="relative mx-auto hidden w-full max-w-lg md:block" role="search" @submit.prevent="submitSearch">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3.5 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="q" type="search" placeholder="Search phones, laptops, audio…" class="bg-muted border-transparent rounded-full pl-10" aria-label="Search products" @focus="searchFocused = true" @blur="searchFocused = false" />
        </form>

        <div class="ml-auto flex items-center gap-1 md:ml-0">
          <NotificationSound /><ThemeToggle />
          <UserMenu />
          <div v-if="!user" class="flex items-center gap-1">
            <Button variant="ghost" size="sm" as-child class="hidden sm:inline-flex"><Link href="/login" prefetch>Log in</Link></Button>
            <Button size="sm" as-child><Link href="/register" prefetch>Sign up</Link></Button>
          </div>
        </div>
      </div>

      <!-- Mobile: search is always one tap away -->
      <form class="px-4 pb-3 md:hidden" role="search" @submit.prevent="submitSearch">
        <div class="relative">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3.5 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="q" type="search" placeholder="Search products" class="bg-muted border-transparent rounded-full pl-10" aria-label="Search products" @focus="searchFocused = true" @blur="searchFocused = false" />
        </div>
      </form>

      <div v-if="mobileOpen" class="border-t md:hidden">
        <nav class="mx-auto flex max-w-7xl flex-col px-4 py-3" aria-label="Mobile navigation">
          <Link v-for="l in shop" :key="l.href" :href="l.href" prefetch class="flex min-h-11 items-center text-base font-medium">{{ l.label }}</Link>
          <p class="text-muted-foreground mt-3 mb-1 text-xs font-medium">Categories</p>
          <Link v-for="c in categories" :key="c.id" :href="`/products?category=${c.slug}`" prefetch class="flex min-h-11 items-center justify-between text-[15px]">
            {{ c.name }} <span class="text-muted-foreground price text-xs">{{ c.count }}</span>
          </Link>
          <p class="text-muted-foreground mt-3 mb-1 text-xs font-medium">Help</p>
          <Link v-for="l in help" :key="l.href" :href="l.href" prefetch class="flex min-h-11 items-center text-[15px]">{{ l.label }}</Link>
        </nav>
      </div>
    </header>

    <main id="main" class="flex-1">
      <slot />
    </main>

    <footer class="bg-card mt-20 border-t">
      <div class="mx-auto grid max-w-7xl gap-10 px-4 py-12 sm:px-6 md:grid-cols-[1.4fr_1fr_1fr_1fr] lg:px-8">
        <div class="space-y-3">
          <BrandMark />
          <p class="text-muted-foreground max-w-xs text-sm leading-6">
            Electronics with manufacturer warranty, optional device insurance and installment plans. Pay by card, mobile money or on delivery.
          </p>
        </div>
        <nav aria-label="Shop" class="space-y-2.5 text-sm">
          <p class="font-medium">Shop</p>
          <Link href="/products" prefetch class="text-muted-foreground hover:text-foreground block">All products</Link>
          <Link href="/deals" prefetch class="text-muted-foreground hover:text-foreground block">Deals</Link>
          <Link v-for="c in categories.slice(0, 4)" :key="c.id" :href="`/products?category=${c.slug}`" prefetch class="text-muted-foreground hover:text-foreground block">{{ c.name }}</Link>
        </nav>
        <nav aria-label="Help" class="space-y-2.5 text-sm">
          <p class="font-medium">Help</p>
          <Link href="/support/policy" prefetch class="text-muted-foreground hover:text-foreground block">Delivery &amp; payments</Link>
          <Link href="/support/policy" prefetch class="text-muted-foreground hover:text-foreground block">Warranty &amp; insurance</Link>
          <Link href="/about" prefetch class="text-muted-foreground hover:text-foreground block">About</Link>
        </nav>
        <nav aria-label="Account" class="space-y-2.5 text-sm">
          <p class="font-medium">Account</p>
          <Link href="/account" prefetch class="text-muted-foreground hover:text-foreground block">My account</Link>
          <Link href="/account/orders" prefetch class="text-muted-foreground hover:text-foreground block">Orders &amp; tracking</Link>
          <Link href="/account/credit" prefetch class="text-muted-foreground hover:text-foreground block">Installment plans</Link>
        </nav>
      </div>
      <div class="border-t">
        <p class="text-muted-foreground mx-auto max-w-7xl px-4 py-5 text-xs sm:px-6 lg:px-8">&copy; {{ new Date().getFullYear() }} TechLandier</p>
      </div>
    </footer>

    <Toaster position="bottom-right" close-button />
  </div>
</template>
