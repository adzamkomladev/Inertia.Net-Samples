<script setup lang="ts">
import { computed, ref, watch, type ComponentPublicInstance } from 'vue'
import { InfiniteScroll } from '@inertiajs/vue3'
import type { ReloadOptions } from '@inertiajs/core'
import { useWindowVirtualizer } from '@tanstack/vue-virtual'
import { useMounted } from '@vueuse/core'
import LoadingRows from '@/components/LoadingRows.vue'
import PostCard from '@/components/PostCard.vue'
import type { PostSummary, ScrollPage } from '@/types'

const props = defineProps<{ posts: ScrollPage<PostSummary>; params?: ReloadOptions }>()
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
  count: props.posts.data.length, estimateSize: () => 360, overscan: 5,
  getItemKey: (index: number) => props.posts.data[index]!.id,
  scrollMargin: margin.value,
})))
const measure = (element: Element | ComponentPublicInstance | null) => { if (element instanceof HTMLElement) virtual.value.measureElement(element) }
// Rows from later pages ease in (staggered) the first time they appear; rows present on first render never animate.
const fresh = new Map<string, number>()
const done = new Set<string>()
watch(() => props.posts.data.length, (_, prev) => {
  props.posts.data.slice(prev).forEach((post, i) => fresh.set(String(post.id), Math.min(i, 6) * 60))
})
const rise = (id: string) => {
  const delay = done.has(String(id)) ? undefined : fresh.get(String(id))
  return { class: delay === undefined ? undefined : 'animate-rise', style: delay ? { animationDelay: `${delay}ms` } : undefined }
}
const markDone = (event: AnimationEvent, id: string) => { if (event.target === event.currentTarget) done.add(String(id)) }
</script>

<template>
  <!-- A custom `end-element` means Inertia never renders its footer, so the idle `#loading` slot
       would never mount; the scoped `loadingNext` flag from the default slot drives the skeleton
       that appears at the bottom of the feed while the next page is retrieved in the background. -->
  <InfiniteScroll :params="params" data="posts" only-next preserve-url :buffer="600" :end-element="() => end">
    <template #default="{ loadingNext }">
      <div :ref="setList" role="list" aria-label="Posts" :style="isMounted ? { height: `${virtual.getTotalSize()}px`, position: 'relative' } : undefined">
        <template v-if="isMounted">
          <div
            v-for="row in virtual.getVirtualItems()" :key="String(row.key)" :ref="measure" :data-index="row.index" role="listitem"
            class="absolute top-0 left-0 w-full" :style="{ transform: `translateY(${row.start - margin}px)` }"
          >
            <div v-bind="rise(posts.data[row.index]!.id)" @animationend="markDone($event, posts.data[row.index]!.id)">
              <PostCard :post="posts.data[row.index]!" />
            </div>
          </div>
        </template>
        <div v-for="post in isMounted ? [] : posts.data" :key="post.id" role="listitem">
          <div v-bind="rise(post.id)" @animationend="markDone($event, post.id)"><PostCard :post="post" /></div>
        </div>
      </div>
      <div ref="end" class="h-px" aria-hidden="true" />
      <!-- Sits after the sentinel so it can never shift the observer geometry; it covers exactly
           the background retrieval window and the arriving rows take over with their own rise. -->
      <LoadingRows v-if="loadingNext" kind="posts" :rows="2" label="Loading more posts" />
    </template>
  </InfiniteScroll>
</template>
