<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link } from '@inertiajs/vue3'
import { Bookmark, Compass, Newspaper } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import LinkTabs from '@/components/LinkTabs.vue'
import PageHeader from '@/components/PageHeader.vue'
import VirtualPostList from '@/components/VirtualPostList.vue'
import { Button } from '@/components/ui/button'
import type { PostSummary, ScrollPage } from '@/types'

const props = defineProps<{ posts: ScrollPage<PostSummary>; title: string; mode: string; filters: { q: string } }>()
const ranked = computed(() => props.mode === 'for-you' || props.mode === 'following')
const saved = computed(() => props.mode === 'bookmarks')
const descriptions: Record<string, string> = {
  'for-you': 'Picked from the people and topics you engage with.',
  following: 'The latest from people you follow.',
  bookmarks: 'Posts you saved for later.',
  mine: 'Everything you have published.',
  archives: 'Hidden from feeds. Restore a post any time from its menu.',
}
const tabs = [
  { href: '/feed?mode=for-you', label: 'For you', value: 'for-you' },
  { href: '/feed?mode=following', label: 'Following', value: 'following' },
]
const emptyTitle = computed(() => ({
  bookmarks: 'No bookmarks yet',
  mine: 'You have not published anything yet',
  archives: 'No archived posts',
  following: 'Your following feed is empty',
} as Record<string, string>)[props.mode] ?? 'Nothing here yet')
</script>

<template>
  <Head :title="title" />
  <section class="mx-auto max-w-2xl">
    <PageHeader :title="ranked ? 'Your feed' : title" :description="descriptions[mode]" />
    <div v-if="ranked" class="mt-5"><LinkTabs label="Feed mode" :current="mode" :tabs="tabs" /></div>
    <div :class="ranked ? '' : 'mt-4 border-t'">
      <VirtualPostList v-if="posts.data.length" :posts="posts" />
      <EmptyState
        v-else :icon="saved ? Bookmark : ranked ? Compass : Newspaper" :title="emptyTitle"
        :description="saved ? 'Tap the bookmark on any post to keep it here.' : ranked ? 'Follow a few writers or like some posts and this feed will fill up.' : 'New posts will show up here as soon as they are published.'"
      >
        <Button as-child variant="outline" size="sm" class="rounded-full">
          <Link :href="saved ? '/posts' : '/users'">{{ saved ? 'Browse posts' : 'Find people to follow' }}</Link>
        </Button>
      </EmptyState>
    </div>
  </section>
</template>
