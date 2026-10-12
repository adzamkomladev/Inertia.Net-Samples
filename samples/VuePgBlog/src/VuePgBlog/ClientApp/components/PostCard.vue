<script setup lang="ts">
import { computed } from 'vue'
import { Link } from '@inertiajs/vue3'
import { Eye, Repeat2 } from '@lucide/vue'
import PostActionBar from '@/components/PostActionBar.vue'
import PostActions from '@/components/PostActions.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import type { PostSummary } from '@/types'

const props = defineProps<{ post: PostSummary }>()
const initials = computed(() => props.post.author.name.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase())
const compact = new Intl.NumberFormat('en', { notation: 'compact' })
</script>

<!-- Feed row: avatar rail, author line, serif title, excerpt, cover and actions. Rows are separated by a hairline. -->
<template>
  <article class="group/card relative flex gap-3.5 border-b py-5 sm:gap-4">
    <Link :href="`/users/${post.author.id}`" class="relative shrink-0 self-start rounded-full">
      <Avatar class="size-10">
        <AvatarImage v-if="post.author.avatarUrl" :src="post.author.avatarUrl" alt="" />
        <AvatarFallback class="text-xs font-medium">{{ initials }}</AvatarFallback>
      </Avatar>
    </Link>

    <div class="min-w-0 flex-1">
      <p v-if="post.reposter" class="text-muted-foreground mb-1 flex items-center gap-1.5 text-xs">
        <Repeat2 class="text-repost size-3.5" aria-hidden="true" />
        <Link :href="`/users/${post.reposter.id}`" class="hover:text-foreground font-medium">{{ post.reposter.name }}</Link> reposted
      </p>

      <div class="flex items-center gap-1.5 text-sm">
        <Link :href="`/users/${post.author.id}`" class="truncate font-medium hover:underline">{{ post.author.name }}</Link>
        <span aria-hidden="true" class="text-muted-foreground/60">·</span>
        <RelativeTime :iso="post.createdAt" class="text-muted-foreground shrink-0" />
        <span v-if="post.archivedAt" class="bg-muted text-muted-foreground shrink-0 rounded-md px-1.5 py-0.5 text-[11px] font-medium">Archived</span>
        <span class="ml-auto flex shrink-0 items-center gap-1">
          <span class="text-muted-foreground flex items-center gap-1 text-xs tabular-nums" :title="`${post.viewCount} views`">
            <Eye class="size-3.5" aria-hidden="true" />{{ compact.format(post.viewCount) }}<span class="sr-only"> views</span>
          </span>
          <PostActions :post="post" />
        </span>
      </div>
      <p v-if="post.reason" class="text-muted-foreground text-xs">{{ post.reason }}</p>

      <Link :href="`/posts/${post.slug}`" prefetch cache-for="30s" class="mt-1.5 block rounded-md">
        <h2 class="group-hover/card:text-foreground/80 font-serif text-[21px] leading-snug font-semibold tracking-[-0.01em] text-balance transition-colors">{{ post.title }}</h2>
        <!-- The server HTML-escapes the snippet and only restores <mark> tags. -->
        <p
          v-if="post.snippet"
          class="text-muted-foreground [&_mark]:bg-brand/20 mt-1 line-clamp-2 text-[15px] leading-6 [&_mark]:rounded-sm [&_mark]:px-0.5 [&_mark]:text-foreground"
          v-html="post.snippet"
        />
        <p v-else-if="post.excerpt" class="text-muted-foreground mt-1 line-clamp-2 text-[15px] leading-6">{{ post.excerpt }}</p>
        <div v-if="post.coverUrl" class="bg-muted mt-3 overflow-hidden rounded-xl border">
          <img :src="post.coverUrl" alt="" loading="lazy" class="aspect-[16/9] w-full object-cover transition-transform duration-500 group-hover/card:scale-[1.015]" />
        </div>
      </Link>

      <div v-if="post.topics?.length" class="mt-2.5 flex flex-wrap gap-x-3 gap-y-1">
        <span v-for="topic in post.topics" :key="topic" class="text-muted-foreground text-[13px]">#{{ topic }}</span>
      </div>

      <PostActionBar :post="post" class="mt-2" />
    </div>
  </article>
</template>
