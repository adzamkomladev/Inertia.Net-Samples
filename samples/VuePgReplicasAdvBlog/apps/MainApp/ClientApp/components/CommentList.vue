<script setup lang="ts">
import { computed, onScopeDispose, ref, watch, type ComponentPublicInstance } from 'vue'
import { useMounted } from '@vueuse/core'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { Link, router, useHttp, usePage } from '@inertiajs/vue3'
import { Heart, Loader2, MessageCircle, Trash2 } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { patchEntity, refused } from '@/composables/useSocialToggle'
import type { CommentItem } from '@/types'

const props = defineProps<{ postId: string; comments: CommentItem[]; archived?: boolean }>()
const user = computed(() => usePage().props.auth.user)
const visible = ref(props.comments.map(c => ({ ...c })))
const scroll = ref<HTMLElement | null>(null)
const mounted = useMounted()
const nextCursor = ref<string | null>(null)
const pagination = useHttp<Record<string, never>, { data: CommentItem[]; nextCursor: string | null }>({})
const loading = computed(() => pagination.processing)
const paginationStarted = ref(false)
const loadError = ref('')
onScopeDispose(() => pagination.cancel())
const pending = ref(new Set<string>())
// Only animate after a click, never when a liked comment first renders.
const bumped = ref<string | null>(null)
watch(() => props.comments, comments => {
  const byId = new Map(visible.value.map(c => [c.id, c]))
  comments.forEach(c => byId.set(c.id, { ...c }))
  visible.value = [...byId.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id))
  // Fresh deferred props may update likes or add replies, but must not restart an exhausted cursor.
  if (!paginationStarted.value) nextCursor.value = comments.length === 50 ? comments[49]!.cursor ?? null : null
}, { immediate: true })
const virtual = useVirtualizer(computed(() => ({ count: visible.value.length, getScrollElement: () => scroll.value, estimateSize: () => 120, overscan: 5, getItemKey: (i: number) => visible.value[i]!.id })))
const rows = computed(() => mounted.value ? virtual.value.getVirtualItems().map(row => ({ c: visible.value[row.index]!, row })) : visible.value.map(c => ({ c, row: null })))
const measure = (element: Element | ComponentPublicInstance | null) => { if (element instanceof HTMLElement && mounted.value) virtual.value.measureElement(element) }
async function loadMore() {
  if (!nextCursor.value || loading.value) return
  paginationStarted.value = true
  loadError.value = ''
  try {
    const result = await pagination.get(`/posts/${props.postId}/comments?cursor=${encodeURIComponent(nextCursor.value)}`)
    const known = new Set(visible.value.map(c => c.id))
    visible.value.push(...result.data.filter(c => !known.has(c.id)))
    nextCursor.value = result.nextCursor
  } catch { loadError.value = 'Unable to load older comments. Please try again.' }
}
// Keep the `comments` prop (and so the history snapshot) in sync with what the server confirmed.
const patchComments = (update: (items: CommentItem[]) => CommentItem[]) =>
  router.replaceProp('comments', (items: unknown) => Array.isArray(items) ? update(items as CommentItem[]) : items)
const like = (comment: CommentItem) => {
  if (pending.value.has(comment.id)) return
  pending.value.add(comment.id)
  const previous = { liked: comment.liked, count: comment.likeCount ?? 0 }
  const next = { liked: !previous.liked, likeCount: Math.max(0, previous.count + (previous.liked ? -1 : 1)) }
  let confirmed = false
  Object.assign(comment, next)
  bumped.value = comment.id
  const options = {
    only: ['auth'], async: true, preserveScroll: true, preserveState: true,
    onSuccess: (page: { flash?: unknown }) => { if (refused(page)) return; confirmed = true; patchComments(items => items.map(c => c.id === comment.id ? { ...c, ...next } : c)) },
    onFinish: () => {
      pending.value.delete(comment.id)
      if (!confirmed) Object.assign(comment, { liked: previous.liked, likeCount: previous.count })
    },
  }
  const url = `/comments/${comment.id}/like`
  if (previous.liked) router.delete(url, options)
  else router.put(url, {}, options)
}
const remove = (id: string) => router.delete(`/comments/${id}`, {
  only: ['auth'],
  async: true,
  preserveScroll: true,
  preserveState: true,
  onSuccess: page => {
    if (refused(page)) return
    visible.value = visible.value.filter(c => c.id !== id)
    patchComments(items => items.filter(c => c.id !== id))
    patchEntity('post', props.postId, post => ({ ...post, commentCount: Math.max(0, Number(post.commentCount ?? 0) - 1) }))
  },
})
const initials = (n: string) => n.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase()
</script>

<template>
  <EmptyState v-if="!visible.length" :icon="MessageCircle" title="No comments yet" description="Start the conversation." class="py-10" />
  <div v-else ref="scroll" scroll-region class="focus-visible:ring-ring/50 max-h-[60vh] overflow-y-auto outline-none focus-visible:ring-2" tabindex="0" aria-label="Comments list">
    <ul :style="mounted ? { height: `${virtual.getTotalSize()}px`, position: 'relative' } : undefined">
      <li
        v-for="{ c, row } in rows" :id="`comment-${c.id}`" :key="c.id" :ref="measure" :data-index="row?.index"
        class="group flex w-full scroll-mt-20 gap-3 pb-5" :style="row ? { position: 'absolute', top: 0, left: 0, transform: `translateY(${row.start}px)` } : undefined"
      >
        <Avatar class="size-9 shrink-0">
          <AvatarImage v-if="c.author.avatarUrl" :src="c.author.avatarUrl" alt="" />
          <AvatarFallback class="text-xs">{{ initials(c.author.name) }}</AvatarFallback>
        </Avatar>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-1.5 text-sm">
            <Link :href="`/users/${c.author.id}`" class="truncate font-medium hover:underline">{{ c.author.name }}</Link>
            <span aria-hidden="true" class="text-muted-foreground/60">·</span>
            <RelativeTime :iso="c.createdAt" class="text-muted-foreground shrink-0" />
            <AlertDialog v-if="c.canDelete && !archived">
              <AlertDialogTrigger as-child>
                <Button
                  variant="ghost" size="icon-xs" :aria-label="`Delete comment by ${c.author.name}`"
                  class="text-muted-foreground hover:bg-destructive/10 hover:text-destructive ml-auto rounded-full focus-visible:opacity-100 [@media(hover:hover)]:opacity-0 [@media(hover:hover)]:group-hover:opacity-100"
                >
                  <Trash2 />
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Delete this comment?</AlertDialogTitle>
                  <AlertDialogDescription>This cannot be undone.</AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Cancel</AlertDialogCancel>
                  <AlertDialogAction @click="remove(c.id)">Delete</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
          <p class="mt-0.5 text-[15px] leading-relaxed break-words whitespace-pre-wrap">{{ c.body }}</p>
          <button
            type="button" :disabled="archived || !user?.verified" :aria-busy="pending.has(c.id)" :aria-pressed="!!c.liked"
            :class="cn('group/like text-muted-foreground hover:text-like focus-visible:ring-ring -ml-1.5 mt-1 flex items-center gap-1 rounded-full pr-2 text-xs transition-colors outline-none focus-visible:ring-2 disabled:pointer-events-none disabled:opacity-50', c.liked && 'text-like')"
            @click="like(c)"
          >
            <span class="group-hover/like:bg-like/10 flex size-6 items-center justify-center rounded-full transition-colors">
              <Heart
                :class="cn('size-3.5', c.liked && 'fill-current', bumped === c.id && c.liked && 'animate-pop')"
                aria-hidden="true" @animationend="bumped = null"
              />
            </span>
            <span class="tabular-nums">{{ c.likeCount || '' }}</span>
            <span class="sr-only">{{ c.liked ? 'Unlike comment' : 'Like comment' }}</span>
          </button>
        </div>
      </li>
    </ul>
  </div>
  <p v-if="loadError" role="alert" class="text-destructive text-sm">{{ loadError }}</p>
  <div v-if="nextCursor" class="flex justify-center">
    <Button variant="secondary" size="sm" class="text-muted-foreground rounded-full" :disabled="loading" @click="loadMore">
      <Loader2 v-if="loading" class="animate-spin" /> Load older comments
    </Button>
  </div>
</template>
