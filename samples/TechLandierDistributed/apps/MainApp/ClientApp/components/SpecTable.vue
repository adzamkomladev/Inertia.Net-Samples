<script setup lang="ts">
import type { SpecRow } from '@/types'

defineProps<{ specs: SpecRow[] }>()
</script>

<!-- Spec sheet: hairline rows, label left, value right. -->
<template>
  <dl v-if="specs.length" class="divide-y">
    <div v-for="row in specs" :key="row.label" class="grid grid-cols-[minmax(6rem,12rem)_1fr] gap-4 py-3 text-sm">
      <dt class="text-muted-foreground">{{ row.label }}</dt>
      <dd class="price">{{ row.value }}</dd>
    </div>
  </dl>
</template>
