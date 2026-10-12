<script setup lang="ts">
import { computed } from 'vue'
import { usePage } from '@inertiajs/vue3'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { useSocialToggle } from '@/composables/useSocialToggle'
import type { UserSummary } from '@/types'

const props = defineProps<{ user: UserSummary }>()
const page = usePage()
const viewer = computed(() => page.props.auth.user)
const follow = useSocialToggle({
  url: () => `/users/${props.user.id}/follow`, kind: 'user', id: () => props.user.id, field: 'following',
  active: () => props.user.following, count: () => props.user.followerCount, countField: 'followerCount',
})
</script>

<template>
  <Button
    v-if="viewer && viewer.id !== user.id" type="button" size="sm"
    :variant="follow.active.value ? 'outline' : 'default'" :disabled="follow.pending.value" :aria-pressed="follow.active.value"
    :class="cn('group/follow h-8 min-w-24 rounded-full px-4', follow.active.value && 'hover:border-destructive/40 hover:bg-destructive/10 hover:text-destructive')"
    @click="follow.toggle"
  >
    <template v-if="follow.active.value">
      <span class="group-hover/follow:hidden">Following</span>
      <span class="hidden group-hover/follow:inline">Unfollow</span>
    </template>
    <template v-else>Follow</template>
  </Button>
</template>
