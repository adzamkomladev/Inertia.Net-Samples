<script setup lang="ts">
import { computed, ref, watch, type Component } from 'vue'
import { Link, usePage } from '@inertiajs/vue3'
import {
  ArrowLeft, Bell, CreditCard, Home, LayoutGrid, LifeBuoy, Package, ShieldCheck, SlidersHorizontal, Wrench,
} from '@lucide/vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useTheme } from '@/composables/useTheme'
import { useRealtime } from '@/composables/useRealtime'
import { cn } from '@/lib/utils'

type NavEntry = { href: string; label: string; icon: Component; exact?: boolean }

const page = usePage()
const user = computed(() => page.props.auth.user)
const initials = computed(() => user.value?.name.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase() ?? '')

useTheme()
useRealtime(computed(() => user.value?.id))
useFlashToast()

const nav: NavEntry[] = [
  { href: '/account', label: 'Overview', icon: LayoutGrid, exact: true },
  { href: '/account/orders', label: 'Orders', icon: Package },
  { href: '/account/credit', label: 'Credit & BNPL', icon: CreditCard },
  { href: '/account/care/warranties', label: 'Warranties', icon: ShieldCheck },
  { href: '/account/care/insurance', label: 'Insurance', icon: ShieldCheck },
  { href: '/account/care/repairs', label: 'Repairs', icon: Wrench },
  { href: '/account/support', label: 'Support', icon: LifeBuoy },
  { href: '/account/alerts', label: 'Alerts', icon: Bell },
  { href: '/account/preferences', label: 'Preferences', icon: SlidersHorizontal },
]

const isActive = (entry: NavEntry) => {
  const url = page.url.split('?')[0]!
  if (entry.exact) return url === entry.href
  return url === entry.href || url.startsWith(`${entry.href}/`)
}

const mobileNavOpen = ref(false)
watch(() => page.url, () => (mobileNavOpen.value = false))
</script>

<template>
  <div class="bg-background flex min-h-screen">
    <!-- Desktop sidebar -->
    <aside class="bg-card fixed inset-y-0 left-0 z-30 hidden w-72 flex-col border-r px-4 py-5 lg:flex">
      <Link href="/" prefetch class="focus-visible:ring-ring flex items-center gap-2.5 rounded-xl px-2 py-1 outline-none focus-visible:ring-2">
        <span class="from-brand to-primary text-brand-foreground flex size-8 items-center justify-center rounded-lg bg-gradient-to-br" aria-hidden="true">
          <Home class="size-4" />
        </span>
        <span class="min-w-0">
          <span class="font-display block text-sm font-bold tracking-tight">{{ page.props.appName }}</span>
          <span class="text-muted-foreground block text-xs">Customer account</span>
        </span>
      </Link>

      <nav aria-label="Account navigation" class="mt-6 flex flex-col gap-0.5">
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
          <Link href="/" prefetch><ArrowLeft class="size-4" /> Back to store</Link>
        </Button>
      </div>
    </aside>

    <div class="min-w-0 flex-1 lg:pl-72">
      <!-- Mobile top bar -->
      <header class="glass sticky top-0 z-30 border-b lg:hidden">
        <div class="flex h-14 items-center justify-between gap-3 px-4">
          <Link href="/" prefetch class="flex items-center gap-2 font-semibold">
            <span class="from-brand to-primary text-brand-foreground flex size-7 items-center justify-center rounded-lg bg-gradient-to-br" aria-hidden="true">
              <Home class="size-3.5" />
            </span>
            My account
          </Link>
          <Button variant="outline" size="sm" class="rounded-xl" @click="mobileNavOpen = !mobileNavOpen">
            <ArrowLeft class="size-4" /> Store
          </Button>
        </div>
        <nav v-if="mobileNavOpen" aria-label="Mobile account navigation" class="flex flex-col gap-1 border-t p-3">
          <Link
            v-for="entry in nav" :key="entry.href" :href="entry.href" prefetch
            :class="cn('flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm hover:bg-accent', isActive(entry) && 'bg-accent font-medium')"
          >
            <component :is="entry.icon" class="size-4" aria-hidden="true" />
            {{ entry.label }}
          </Link>
        </nav>
      </header>

      <main class="animate-rise mx-auto w-full max-w-5xl px-4 py-6 pb-24 sm:px-6 lg:px-10 lg:py-10">
        <slot />
      </main>
    </div>

    <Toaster position="bottom-right" close-button />
  </div>
</template>
