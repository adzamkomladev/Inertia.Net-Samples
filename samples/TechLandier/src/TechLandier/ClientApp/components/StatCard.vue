<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{ label: string; value: string | number; icon?: Component; hint?: string }>()
</script>

<!-- KPI tile used across the account and admin dashboards. -->
<template>
  <div class="bg-card animate-rise rounded-2xl border p-4 shadow-xs sm:p-5">
    <div class="flex items-center justify-between gap-2">
      <p class="text-muted-foreground text-sm">{{ label }}</p>
      <span v-if="icon" class="bg-accent text-accent-foreground flex size-8 items-center justify-center rounded-xl">
        <component :is="icon" class="size-4" :stroke-width="1.75" aria-hidden="true" />
      </span>
    </div>
    <p class="price mt-1 text-2xl font-semibold tracking-tight">{{ typeof value === 'number' ? value.toLocaleString() : value }}</p>
    <p v-if="hint" class="text-muted-foreground mt-0.5 text-xs">{{ hint }}</p>
  </div>
</template>
