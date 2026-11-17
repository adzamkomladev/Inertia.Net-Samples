<script setup lang="ts">
import type { SpecRow } from '@/types'

defineProps<{ specs: SpecRow[] }>()
</script>

<!-- Spec sheet: hairline rows, label left, value right. -->
<template>
  <dl v-if="specs.length" class="divide-y">
    <div v-for="row in specs" :key="row.label" class="grid grid-cols-1 gap-x-4 gap-y-0.5 sm:grid-cols-[minmax(6rem,12rem)_1fr] py-3 text-sm">
      <dt class="text-muted-foreground">{{ row.label }}</dt>
      <dd class="price min-w-0 break-words">{{ row.value }}</dd>
    </div>
  </dl>
</template>
