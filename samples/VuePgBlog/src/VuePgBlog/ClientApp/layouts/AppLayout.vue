<script setup lang="ts">
import { computed, ref, watch, type Component } from 'vue'
import { Link, router, usePage } from '@inertiajs/vue3'
import {
  Archive, BarChart3, Bookmark, Compass, FileText, Home, LogIn, LogOut, MoreHorizontal,
  PenLine, Search, Settings, UserRound, Users, X,
} from '@lucide/vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Toaster } from '@/components/ui/sonner'
import SearchBox from '@/components/SearchBox.vue'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useTheme } from '@/composables/useTheme'
import { useRealtime } from '@/composables/useRealtime'
import { cn } from '@/lib/utils'

type NavEntry = { href: string; label: string; icon: Component }

const page = usePage()
const user = computed(() => page.props.auth.user)
const initials = computed(() => user.value?.name.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase() ?? '')
const q = ref((page.props.filters as { q?: string } | undefined)?.q ?? '')
const searching = ref(false)
// Keep the search box in sync with the URL (back/forward, links, clearing the query).
watch(() => (page.props.filters as { q?: string } | undefined)?.q ?? '', v => { q.value = v })

useTheme()
useRealtime(computed(() => user.value?.id))
useFlashToast()

// Prefetched pages carry the old viewer's props (liked, canEdit, auth): drop them whenever identity or verification changes.
watch(() => `${user.value?.id}:${user.value?.verified}`, () => router.flushAll())

const logout = () => {
  router.flushAll()
  router.post('/logout')
}

const isActive = (href: string) => {
  const url = page.url
  const path = href.split('?')[0]!
  if (href.includes('?')) return url.split('&')[0] === href
  if (path === '/posts') return url === '/posts' || url.startsWith('/posts?') || url === '/'
  return url === href || url.startsWith(`${path}?`) || url.startsWith(`${path}/`)
}

const primary = computed<NavEntry[]>(() => [
  { href: '/posts', label: 'Home', icon: Home },
  ...(user.value ? [
    { href: '/feed?mode=for-you', label: 'For you', icon: Compass },
    { href: '/feed?mode=following', label: 'Following', icon: Users },
  ] : []),
  { href: '/users', label: 'People', icon: UserRound },
])
const library = computed<NavEntry[]>(() => user.value ? [
  { href: '/bookmarks', label: 'Bookmarks', icon: Bookmark },
  { href: '/me/posts', label: 'My posts', icon: FileText },
  { href: '/analytics', label: 'Analytics', icon: BarChart3 },
] : [])
const tabs = computed<NavEntry[]>(() => user.value
  ? [primary.value[0]!, primary.value[1]!, ...(user.value.verified ? [{ href: '/posts/create', label: 'Write', icon: PenLine }] : []), { href: '/users', label: 'People', icon: UserRound }, { href: '/bookmarks', label: 'Saved', icon: Bookmark }]
  : [primary.value[0]!, { href: '/users', label: 'People', icon: UserRound }, { href: '/login', label: 'Log in', icon: LogIn }])
const linkClass = (href: string) => cn(
  'group text-muted-foreground hover:bg-accent hover:text-foreground flex items-center gap-3 rounded-lg px-3 py-2 text-[15px] transition-colors',
  isActive(href) && 'bg-accent text-foreground font-medium',
)
</script>

<template>
  <div class="bg-background min-h-screen">
    <!-- Desktop sidebar -->
    <aside class="bg-background fixed inset-y-0 left-0 z-30 hidden w-64 flex-col border-r px-3 py-4 lg:flex">
      <Link href="/posts" prefetch class="mb-5 flex items-center gap-2.5 rounded-lg px-2 py-1 text-[17px] font-semibold tracking-tight">
        <span class="bg-foreground text-background flex size-8 items-center justify-center rounded-lg font-serif text-lg leading-none font-semibold" aria-hidden="true">{{ page.props.appName.charAt(0) }}</span>
        {{ page.props.appName }}
      </Link>
      <div class="mb-4 px-1"><SearchBox v-model="q" /></div>
      <nav aria-label="Main navigation" class="flex flex-col gap-0.5">
        <Link v-for="item in primary" :key="item.href" :href="item.href" prefetch :aria-current="isActive(item.href) ? 'page' : undefined" :class="linkClass(item.href)">
          <component :is="item.icon" :class="cn('size-[18px] transition-transform group-active:scale-90', isActive(item.href) && 'text-brand')" :stroke-width="isActive(item.href) ? 2.25 : 1.75" aria-hidden="true" />
          {{ item.label }}
        </Link>
      </nav>
      <nav v-if="library.length" aria-label="Your library" class="mt-6 flex flex-col gap-0.5">
        <p class="text-muted-foreground px-3 pb-1.5 text-xs font-medium">Library</p>
        <Link v-for="item in library" :key="item.href" :href="item.href" prefetch :aria-current="isActive(item.href) ? 'page' : undefined" :class="linkClass(item.href)">
          <component :is="item.icon" :class="cn('size-[18px] transition-transform group-active:scale-90', isActive(item.href) && 'text-brand')" :stroke-width="isActive(item.href) ? 2.25 : 1.75" aria-hidden="true" />
          {{ item.label }}
        </Link>
      </nav>
      <Button v-if="user?.verified" as-child class="mt-6 h-10 rounded-lg text-[15px] transition-transform active:scale-[0.98]">
        <Link href="/posts/create" prefetch><PenLine class="size-4" /> Write a post</Link>
      </Button>

      <div class="mt-auto">
        <DropdownMenu v-if="user">
          <DropdownMenuTrigger as-child>
            <button type="button" class="hover:bg-accent flex w-full items-center gap-3 rounded-lg p-2 text-left transition-colors" aria-label="Account menu">
              <Avatar class="size-8">
                <AvatarImage v-if="user.avatarUrl" :src="user.avatarUrl" alt="" />
                <AvatarFallback class="text-xs">{{ initials }}</AvatarFallback>
              </Avatar>
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-medium">{{ user.name }}</span>
                <span class="text-muted-foreground block truncate text-xs">{{ user.email }}</span>
              </span>
              <MoreHorizontal class="text-muted-foreground size-4" aria-hidden="true" />
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start" side="top" class="w-60">
            <DropdownMenuLabel class="font-normal">
              <div class="truncate text-sm font-medium">{{ user.name }}</div>
              <div class="text-muted-foreground truncate text-xs">{{ user.email }}</div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem as-child><Link :href="`/users/${user.id}`" prefetch><UserRound class="size-4" /> Profile</Link></DropdownMenuItem>
            <DropdownMenuItem as-child><Link href="/me/archives" prefetch><Archive class="size-4" /> Archives</Link></DropdownMenuItem>
            <DropdownMenuItem as-child><Link href="/settings/preferences" prefetch><Settings class="size-4" /> Settings</Link></DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem @select="logout"><LogOut class="size-4" /> Log out</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <div v-else class="flex flex-col gap-2 rounded-xl border p-3">
          <p class="text-sm font-medium">Join the conversation</p>
          <p class="text-muted-foreground text-xs leading-5">Follow writers, like posts and build your own feed.</p>
          <Button as-child size="sm" class="mt-1 rounded-lg"><Link href="/register" prefetch>Create account</Link></Button>
          <Button as-child size="sm" variant="ghost" class="rounded-lg"><Link href="/login" prefetch>Log in</Link></Button>
        </div>
      </div>
    </aside>

    <!-- Mobile top bar -->
    <header class="bg-background/80 sticky top-0 z-30 border-b backdrop-blur-md lg:hidden">
      <div class="flex h-14 items-center gap-3 px-4">
        <template v-if="searching">
          <div class="flex-1"><SearchBox v-model="q" /></div>
          <Button variant="ghost" size="icon-sm" aria-label="Close search" @click="searching = false"><X /></Button>
        </template>
        <template v-else>
          <Link href="/posts" prefetch class="flex items-center gap-2 font-semibold tracking-tight">
            <span class="bg-foreground text-background flex size-7 items-center justify-center rounded-lg font-serif text-base leading-none font-semibold" aria-hidden="true">{{ page.props.appName.charAt(0) }}</span>
            {{ page.props.appName }}
          </Link>
          <Button variant="ghost" size="icon-sm" class="ml-auto rounded-full" aria-label="Search" @click="searching = true"><Search /></Button>
          <DropdownMenu v-if="user">
            <DropdownMenuTrigger as-child>
              <button type="button" class="rounded-full" aria-label="Account menu">
                <Avatar class="size-7">
                  <AvatarImage v-if="user.avatarUrl" :src="user.avatarUrl" alt="" />
                  <AvatarFallback class="text-xs">{{ initials }}</AvatarFallback>
                </Avatar>
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" class="w-60">
              <DropdownMenuLabel class="font-normal">
                <div class="truncate text-sm font-medium">{{ user.name }}</div>
                <div class="text-muted-foreground truncate text-xs">{{ user.email }}</div>
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem as-child><Link :href="`/users/${user.id}`" prefetch><UserRound class="size-4" /> Profile</Link></DropdownMenuItem>
              <DropdownMenuItem as-child><Link href="/me/posts" prefetch><FileText class="size-4" /> My posts</Link></DropdownMenuItem>
              <DropdownMenuItem as-child><Link href="/analytics" prefetch><BarChart3 class="size-4" /> Analytics</Link></DropdownMenuItem>
              <DropdownMenuItem as-child><Link href="/me/archives" prefetch><Archive class="size-4" /> Archives</Link></DropdownMenuItem>
              <DropdownMenuItem as-child><Link href="/settings/preferences" prefetch><Settings class="size-4" /> Settings</Link></DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem @select="logout"><LogOut class="size-4" /> Log out</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
          <Button v-else as-child size="sm" class="rounded-full"><Link href="/register" prefetch>Sign up</Link></Button>
        </template>
      </div>
    </header>

    <div class="lg:pl-64">
      <div v-if="user && !user.verified" class="bg-brand/8 border-b px-4 py-2.5 text-center text-sm">
        Verify your email to post, comment and like.
        <Link href="/email/verify" prefetch class="text-brand font-medium underline-offset-4 hover:underline">Resend link</Link>
      </div>
      <main :key="page.url.split('?')[0]" class="animate-rise mx-auto w-full max-w-5xl px-4 pt-6 pb-28 sm:px-6 lg:px-10 lg:pt-10 lg:pb-16">
        <slot />
      </main>
    </div>

    <!-- Mobile tab bar -->
    <nav aria-label="Tabs" class="bg-background/90 fixed inset-x-0 bottom-0 z-30 border-t pb-[env(safe-area-inset-bottom)] backdrop-blur-md lg:hidden">
      <div class="mx-auto flex max-w-md items-stretch justify-around">
        <Link
          v-for="tab in tabs" :key="tab.href" :href="tab.href" prefetch :aria-current="isActive(tab.href) ? 'page' : undefined"
          :class="cn('text-muted-foreground flex flex-1 flex-col items-center gap-1 py-2 text-[11px] transition-colors active:scale-95', isActive(tab.href) && 'text-foreground')"
        >
          <span v-if="tab.href === '/posts/create'" class="bg-foreground text-background flex size-8 items-center justify-center rounded-full">
            <component :is="tab.icon" class="size-4" aria-hidden="true" />
          </span>
          <component :is="tab.icon" v-else :class="cn('size-5', isActive(tab.href) && 'text-brand')" :stroke-width="isActive(tab.href) ? 2.25 : 1.75" aria-hidden="true" />
          <span :class="cn(tab.href === '/posts/create' && 'sr-only')">{{ tab.label }}</span>
        </Link>
      </div>
    </nav>

    <Toaster position="bottom-right" close-button />
  </div>
</template>
