<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{ label: string; value: string | number; icon?: Component; hint?: string; tone?: 'default' | 'warning' }>()
</script>

<!-- KPI tile for account and admin dashboards: label, one big number, one hint. -->
<template>
  <div class="surface p-4 sm:p-5">
    <div class="text-muted-foreground flex items-center justify-between gap-2">
      <p class="text-sm">{{ label }}</p>
      <component :is="icon" v-if="icon" class="size-4" :stroke-width="1.75" aria-hidden="true" />
    </div>
    <p class="price mt-2 text-2xl font-semibold tracking-tight" :class="tone === 'warning' && 'text-warning'">
      {{ typeof value === 'number' ? value.toLocaleString() : value }}
    </p>
    <p v-if="hint" class="text-muted-foreground mt-0.5 text-xs">{{ hint }}</p>
  </div>
</template>
