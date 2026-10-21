<script setup lang="ts">
import type { SpecRow } from '@/types'

defineProps<{ specs: SpecRow[] }>()
</script>

<!-- Two-column spec sheet with mono values. -->
<template>
  <dl v-if="specs.length" class="overflow-hidden rounded-2xl border">
    <div
      v-for="(row, i) in specs"
      :key="row.label"
      :class="i % 2 === 0 ? 'bg-card' : 'bg-muted/40'"
      class="grid grid-cols-[minmax(6rem,11rem)_1fr] gap-4 px-4 py-2.5 text-sm"
    >
      <dt class="text-muted-foreground self-center">{{ row.label }}</dt>
      <dd class="price text-right sm:text-left">{{ row.value }}</dd>
    </div>
  </dl>
</template>
