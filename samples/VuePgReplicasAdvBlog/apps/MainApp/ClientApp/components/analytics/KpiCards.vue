<script setup lang="ts">
import type { Kpis } from '@/types'
import { computed } from 'vue'

const props = defineProps<{ kpis: Kpis; hidePosts?: boolean }>()

const items = computed(() => [
  { label: 'Views', value: props.kpis.views },
  { label: 'Unique viewers', value: props.kpis.uniqueViewers },
  { label: 'Likes', value: props.kpis.likes },
  { label: 'Comments', value: props.kpis.comments },
  ...(props.hidePosts ? [] : [{ label: 'Posts', value: props.kpis.posts }]),
])
</script>

<template>
  <div
    class="grid gap-px overflow-hidden rounded-xl border bg-border *:bg-card"
    :class="hidePosts ? 'grid-cols-2 lg:grid-cols-4' : 'grid-cols-2 lg:grid-cols-5'"
  >
    <div
      v-for="(i, idx) in items"
      :key="i.label"
      class="animate-rise space-y-1.5 p-4 sm:p-5"
      :style="{ animationDelay: `${idx * 40}ms` }"
    >
      <p class="text-sm text-muted-foreground">{{ i.label }}</p>
      <p class="text-2xl font-semibold tabular-nums tracking-tight">{{ i.value.toLocaleString() }}</p>
    </div>
  </div>
</template>