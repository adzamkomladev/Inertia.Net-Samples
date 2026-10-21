<script setup lang="ts">
import { computed } from 'vue'
import { Skeleton } from '@/components/ui/skeleton'

const props = withDefaults(
  defineProps<{ rows: { label: string; value: number; sub?: string | null }[]; format?: (v: number) => string }>(),
  { format: undefined },
)

const max = computed(() => Math.max(1, ...props.rows.map((r) => r.value)))
const fmt = computed(() => props.format ?? ((v: number) => v.toLocaleString()))
</script>

<!-- Horizontal ranked bars for top sellers / category share. -->
<template>
  <ol v-if="rows.length" class="space-y-3">
    <li v-for="(row, i) in rows" :key="row.label" class="space-y-1">
      <div class="flex items-baseline justify-between gap-3 text-sm">
        <span class="min-w-0 truncate">
          <span class="text-muted-foreground price mr-2 text-xs">{{ String(i + 1).padStart(2, '0') }}</span>
          <span class="font-medium">{{ row.label }}</span>
          <span v-if="row.sub" class="text-muted-foreground ml-2 text-xs">{{ row.sub }}</span>
        </span>
        <span class="price shrink-0 font-medium">{{ fmt(row.value) }}</span>
      </div>
      <div class="bg-muted h-1.5 overflow-hidden rounded-full" aria-hidden="true">
        <div
          class="bg-chart-1 animate-rise h-full rounded-full transition-all duration-700 ease-[var(--ease-out-soft)]"
          :style="{ width: `${(row.value / max) * 100}%`, animationDelay: `${i * 60}ms` }"
        />
      </div>
    </li>
  </ol>
  <Skeleton v-else class="h-40 w-full rounded-2xl" />
</template>
