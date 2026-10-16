<script setup lang="ts">
import { Skeleton } from '@/components/ui/skeleton'

const props = withDefaults(
  defineProps<{ kind: 'posts' | 'users'; rows?: number; label?: string }>(),
  { rows: 2, label: 'Loading more' },
)
// Rows fade out down the list: the second row stays visible enough for the "load more" pair,
// while full-page placeholders step down gently across four or five rows.
const fade = (i: number) => (props.rows <= 2 ? 1 - i * 0.4 : Math.max(0.35, 1 - i * 0.22))
</script>

<template>
  <div role="status" :aria-label="label" aria-busy="true" class="animate-in fade-in duration-300">
    <template v-for="i in rows" :key="i">
      <div v-if="kind === 'users'" class="flex items-center gap-3 border-b py-4" :style="{ opacity: fade(i - 1) }">
        <Skeleton class="size-11 shrink-0 rounded-full" />
        <div class="flex-1 space-y-2"><Skeleton class="h-3.5 w-36" /><Skeleton class="h-3 w-24" /></div>
        <Skeleton class="h-8 w-24 rounded-full" />
      </div>
      <div v-else class="flex gap-4 border-b py-5" :style="{ opacity: fade(i - 1) }">
        <Skeleton class="size-10 shrink-0 rounded-full" />
        <div class="flex-1 space-y-3">
          <Skeleton class="h-3.5 w-40" />
          <Skeleton class="h-5 w-4/5" />
          <div class="space-y-2"><Skeleton class="h-3.5 w-full" /><Skeleton class="h-3.5 w-2/3" /></div>
        </div>
      </div>
    </template>
  </div>
</template>
