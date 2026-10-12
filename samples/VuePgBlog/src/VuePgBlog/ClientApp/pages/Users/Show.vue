<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link } from '@inertiajs/vue3'
import { FileText } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import FollowButton from '@/components/FollowButton.vue'
import VirtualPostList from '@/components/VirtualPostList.vue'
import { Button } from '@/components/ui/button'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import type { PostSummary, ScrollPage, UserSummary } from '@/types'

const props = defineProps<{ user: UserSummary; posts: ScrollPage<PostSummary> }>()
const initials = computed(() => props.user.name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase())
</script>

<template>
  <Head :title="user.name" />
  <section class="mx-auto flex max-w-2xl flex-col gap-6">
    <header class="flex flex-wrap items-center gap-x-5 gap-y-4">
      <Avatar class="size-20">
        <AvatarImage v-if="user.avatarUrl" :src="user.avatarUrl" alt="" />
        <AvatarFallback class="text-xl">{{ initials }}</AvatarFallback>
      </Avatar>
      <div class="min-w-0 flex-1 space-y-1">
        <h1 class="truncate text-2xl font-semibold tracking-tight">{{ user.name }}</h1>
        <p class="text-muted-foreground flex gap-4 text-sm tabular-nums">
          <span><span class="text-foreground font-medium">{{ user.followerCount }}</span> followers</span>
          <span><span class="text-foreground font-medium">{{ user.followingCount }}</span> following</span>
        </p>
      </div>
      <Button v-if="user.isSelf" as-child variant="outline" size="sm" class="rounded-full">
        <Link href="/settings/preferences">Edit profile</Link>
      </Button>
      <FollowButton v-else :user="user" />
    </header>

    <div>
      <h2 class="border-b pb-3 text-base font-semibold">Posts</h2>
      <VirtualPostList v-if="posts.data.length" :posts="posts" />
      <EmptyState v-else :icon="FileText" title="No posts yet" :description="`${user.name} has not published anything.`" />
    </div>
  </section>
</template>
