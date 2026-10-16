<script setup lang="ts">
import { computed, ref, watch, type ComponentPublicInstance } from 'vue'
import { useWindowVirtualizer } from '@tanstack/vue-virtual'
import { useMounted } from '@vueuse/core'
import { Head, InfiniteScroll, Link, router } from '@inertiajs/vue3'
import { Search, UserRoundSearch, X } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import FollowButton from '@/components/FollowButton.vue'
import LoadingRows from '@/components/LoadingRows.vue'
import PageHeader from '@/components/PageHeader.vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useDebouncedSearch } from '@/composables/useDebouncedSearch'
import type { ScrollPage, UserSummary } from '@/types'

const props = defineProps<{ users: ScrollPage<UserSummary>; filters: { q: string } }>()
const q = ref(props.filters.q)
useDebouncedSearch(q, '/users')

const list = ref<HTMLElement | null>(null)
const end = ref<HTMLElement | null>(null)
const margin = ref(0)
const isMounted = useMounted()
const setList = (element: Element | ComponentPublicInstance | null) => {
  if (!(element instanceof HTMLElement)) return
  list.value = element
  margin.value = element.getBoundingClientRect().top + window.scrollY
}
const virtual = useWindowVirtualizer(computed(() => ({
  count: props.users.data.length, estimateSize: () => 81, overscan: 5,
  scrollMargin: margin.value, getItemKey: (index: number) => props.users.data[index]!.id,
})))
const rows = computed(() => isMounted.value
  ? virtual.value.getVirtualItems().map(row => ({ person: props.users.data[row.index]!, row }))
  : props.users.data.map(person => ({ person, row: null as { index: number; start: number } | null })))
const measure = (element: Element | ComponentPublicInstance | null) => { if (element instanceof HTMLElement && isMounted.value) virtual.value.measureElement(element) }
// Rows from later pages ease in (staggered) the first time they appear; rows present on first render never animate.
const fresh = new Map<string, number>()
const done = new Set<string>()
watch(() => props.users.data.length, (_, prev) => {
  props.users.data.slice(prev).forEach((person, i) => fresh.set(String(person.id), Math.min(i, 6) * 60))
})
const rise = (id: string) => {
  const delay = done.has(String(id)) ? undefined : fresh.get(String(id))
  return { class: delay === undefined ? undefined : 'animate-rise', style: delay ? { animationDelay: `${delay}ms` } : undefined }
}
const markDone = (event: AnimationEvent, id: string) => { if (event.target === event.currentTarget) done.add(String(id)) }
const initials = (name: string) => name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase()

const search = () => router.get('/users', q.value.trim() ? { q: q.value.trim() } : {}, { replace: true, preserveState: true, preserveScroll: true })
const clear = () => { q.value = '' }
</script>

<template>
  <Head title="People" />
  <section class="mx-auto flex w-full max-w-2xl flex-col gap-6">
    <PageHeader title="People" description="Find writers and follow the ones you like." />

    <form class="relative" role="search" @submit.prevent="search">
      <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
      <Input v-model="q" aria-label="Search people" placeholder="Search people by name…" autocomplete="off" class="h-10 rounded-lg pr-10 pl-10" />
      <button
        v-if="q" type="button" aria-label="Clear search"
        class="text-muted-foreground hover:text-foreground focus-visible:ring-ring absolute top-1/2 right-2 -translate-y-1/2 rounded-md p-1 outline-none focus-visible:ring-2"
        @click="clear"
      >
        <X class="size-4" aria-hidden="true" />
      </button>
    </form>

    <!-- With a custom `end-element` Inertia skips its footer, so the idle `#loading` slot never
         mounts; the scoped `loadingNext` flag drives the skeleton at the bottom while the next
         page of people is retrieved in the background. -->
    <InfiniteScroll v-if="users.data.length" data="users" only-next preserve-url :buffer="600" :end-element="() => end">
      <template #default="{ loadingNext }">
        <div :ref="setList" role="list" aria-label="People" :style="isMounted ? { height: `${virtual.getTotalSize()}px`, position: 'relative' } : undefined">
          <div
            v-for="{ person, row } in rows" :key="person.id" :ref="measure" :data-index="row?.index" role="listitem"
            class="w-full" :style="row ? { position: 'absolute', top: 0, transform: `translateY(${row.start - margin}px)` } : undefined"
          >
            <div v-bind="rise(person.id)" @animationend="markDone($event, person.id)">
              <article class="flex items-center gap-4 border-b py-4">
                <Link :href="`/users/${person.id}`" :aria-label="`View ${person.name}'s profile`" class="focus-visible:ring-ring shrink-0 rounded-full outline-none focus-visible:ring-2">
                  <Avatar class="size-11">
                    <AvatarImage v-if="person.avatarUrl" :src="person.avatarUrl" alt="" />
                    <AvatarFallback>{{ initials(person.name) }}</AvatarFallback>
                  </Avatar>
                </Link>
                <div class="min-w-0 flex-1">
                  <Link :href="`/users/${person.id}`" class="block truncate font-medium hover:underline">{{ person.name }}</Link>
                  <p class="text-muted-foreground truncate text-sm tabular-nums">{{ person.followerCount }} followers · {{ person.followingCount }} following</p>
                </div>
                <FollowButton :user="person" />
              </article>
            </div>
          </div>
        </div>
        <div ref="end" class="h-px" aria-hidden="true" />
        <LoadingRows v-if="loadingNext" kind="users" :rows="2" label="Loading more people" />
      </template>
    </InfiniteScroll>

    <EmptyState
      v-else :icon="UserRoundSearch" :title="filters.q ? `No people match “${filters.q}”` : 'No people yet'"
      :description="filters.q ? 'Try a different name.' : 'People appear here once they join.'"
    >
      <Button v-if="filters.q" variant="link" @click="clear">Clear search</Button>
    </EmptyState>
  </section>
</template>
