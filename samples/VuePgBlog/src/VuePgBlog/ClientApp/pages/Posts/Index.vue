<script setup lang="ts">
import { computed, ref } from 'vue'
import { Head, Link, router, usePage } from '@inertiajs/vue3'
import type { ReloadOptions } from '@inertiajs/core'
import { Newspaper, PenLine, SearchX, X } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import VirtualPostList from '@/components/VirtualPostList.vue'
import PageHeader from '@/components/PageHeader.vue'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import type { PostSummary, ScrollPage } from '@/types'

const props = defineProps<{ posts: ScrollPage<PostSummary>; filters: { q: string }; searchEngine?: 'native' | 'meilisearch' }>()
const page = usePage()
const user = computed(() => page.props.auth.user)
const resetting = ref(false)
const pagination = computed<ReloadOptions>(() => ({
  only: ['searchEngine'],
  data: props.filters.q && props.searchEngine ? { engine: props.searchEngine } : {},
  onBeforeUpdate: page => {
    const actual = page.props.searchEngine
    if (props.filters.q && props.searchEngine && actual && actual !== props.searchEngine) {
      resetting.value = true
      page.props.posts = { ...(page.props.posts as ScrollPage<PostSummary>), data: [] }
    }
  },
  onSuccess: page => {
    const actual = page.props.searchEngine
    if (resetting.value && actual) {
      router.get('/posts', { q: props.filters.q, engine: String(actual) }, { reset: ['posts'], replace: true, preserveState: false, preserveScroll: false, onFinish: () => { resetting.value = false } })
    }
  },
}))
</script>

<template>
  <Head :title="filters.q ? `Results for “${filters.q}”` : 'Posts'" />
  <div class="mx-auto max-w-2xl">
    <PageHeader :description="filters.q ? 'Ranked by relevance across the whole community.' : 'Fresh writing from the community, newest first.'">
      <template #title>
        <template v-if="filters.q">Results for <span class="font-serif italic">“{{ filters.q }}”</span></template>
        <template v-else>Latest</template>
      </template>
      <template #actions>
        <Button v-if="filters.q" as-child variant="outline" size="sm" class="rounded-full"><Link href="/posts"><X class="size-4" /> Clear</Link></Button>
        <Button v-else-if="user?.verified" as-child size="sm" class="rounded-full lg:hidden"><Link href="/posts/create"><PenLine class="size-4" /> Write</Link></Button>
      </template>
    </PageHeader>

    <div class="mt-4 border-t">
      <div v-if="resetting" class="flex flex-col gap-6 py-5" aria-busy="true">
        <div v-for="i in 3" :key="i" class="flex gap-4">
          <Skeleton class="size-10 rounded-full" />
          <div class="flex-1 space-y-2.5"><Skeleton class="h-3.5 w-1/3" /><Skeleton class="h-5 w-4/5" /><Skeleton class="h-3.5 w-full" /></div>
        </div>
        <p role="status" class="sr-only">Refreshing search results…</p>
      </div>
      <EmptyState
        v-else-if="!posts.data.length" :icon="filters.q ? SearchX : Newspaper"
        :title="filters.q ? `No posts match “${filters.q}”` : 'No posts yet'"
        :description="filters.q ? 'Try a different keyword, or browse everything.' : 'Be the first to publish something.'"
      >
        <Button v-if="filters.q" as-child variant="outline" size="sm" class="rounded-full"><Link href="/posts">Show all posts</Link></Button>
        <Button v-else-if="user?.verified" as-child size="sm" class="rounded-full"><Link href="/posts/create"><PenLine class="size-4" /> Write a post</Link></Button>
      </EmptyState>
      <VirtualPostList v-else :key="`${filters.q}:${searchEngine ?? 'native'}`" :params="pagination" :posts="posts" />
    </div>
  </div>
</template>
