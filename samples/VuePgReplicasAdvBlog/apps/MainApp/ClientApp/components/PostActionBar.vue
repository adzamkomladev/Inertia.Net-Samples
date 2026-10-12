<script setup lang="ts">
import { computed } from 'vue'
import { router, usePage } from '@inertiajs/vue3'
import { Bookmark, Heart, MessageCircle, Repeat2 } from '@lucide/vue'
import SocialAction from '@/components/SocialAction.vue'
import { useSocialToggle } from '@/composables/useSocialToggle'
import type { PostSummary } from '@/types'

const props = withDefaults(defineProps<{ post: PostSummary; showBookmark?: boolean }>(), { showBookmark: true })
const page = usePage()
const user = computed(() => page.props.auth.user)
const verified = computed(() => !!user.value?.verified)
const id = () => props.post.id

const like = useSocialToggle({ url: () => `/posts/${props.post.id}/like`, id, field: 'liked', active: () => !!props.post.liked, count: () => props.post.likeCount, countField: 'likeCount' })
const repost = useSocialToggle({ url: () => `/posts/${props.post.id}/repost`, id, field: 'reposted', active: () => !!props.post.reposted, count: () => props.post.repostCount, countField: 'repostCount' })
const bookmark = useSocialToggle({ url: () => `/posts/${props.post.id}/bookmark`, id, field: 'bookmarked', active: () => !!props.post.bookmarked })

const requireVerified = (action: () => void) => verified.value ? action() : router.visit('/email/verify')
</script>

<template>
  <div class="flex items-center gap-5 sm:gap-8">
    <SocialAction :label="post.commentCount ? `${post.commentCount} comments` : 'Comment'" :count="post.commentCount" tone="comment" :href="`/posts/${post.slug}#comments`">
      <MessageCircle :stroke-width="1.75" />
    </SocialAction>

    <template v-if="user">
      <SocialAction
        :label="!verified ? 'Verify email to repost' : repost.active.value ? 'Undo repost' : 'Repost'" :count="repost.count.value" :active="repost.active.value"
        tone="repost" :disabled="!!post.archivedAt || repost.pending.value" @click="requireVerified(repost.toggle)"
      >
        <Repeat2 :stroke-width="1.75" />
      </SocialAction>
      <SocialAction
        :label="!verified ? 'Verify email to like' : like.active.value ? 'Unlike' : 'Like'" :count="like.count.value" :active="like.active.value"
        tone="like" :disabled="!!post.archivedAt || like.pending.value" @click="requireVerified(like.toggle)"
      >
        <Heart :stroke-width="1.75" />
      </SocialAction>
      <span v-if="showBookmark" class="ml-auto">
        <SocialAction
          :label="bookmark.active.value ? 'Remove bookmark' : 'Bookmark'" :active="bookmark.active.value"
          tone="bookmark" :disabled="bookmark.pending.value" @click="bookmark.toggle()"
        >
          <Bookmark :stroke-width="1.75" />
        </SocialAction>
      </span>
    </template>
    <template v-else>
      <SocialAction label="Log in to repost" :count="post.repostCount" tone="repost" href="/login"><Repeat2 :stroke-width="1.75" /></SocialAction>
      <SocialAction label="Log in to like" :count="post.likeCount" tone="like" href="/login"><Heart :stroke-width="1.75" /></SocialAction>
      <span v-if="showBookmark" class="ml-auto"><SocialAction label="Log in to bookmark" tone="bookmark" href="/login"><Bookmark :stroke-width="1.75" /></SocialAction></span>
    </template>
  </div>
</template>
