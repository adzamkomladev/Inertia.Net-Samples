<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{ icon?: Component; title: string; description?: string }>()
</script>

<!-- Quiet empty state used by feeds, people, search results and comments. -->
<template>
  <div class="animate-rise flex flex-col items-center gap-4 px-6 py-20 text-center">
    <span v-if="icon" class="bg-card text-muted-foreground flex size-12 items-center justify-center rounded-2xl border shadow-xs">
      <component :is="icon" class="size-5" :stroke-width="1.75" aria-hidden="true" />
    </span>
    <div class="max-w-sm space-y-1.5">
      <p class="font-medium">{{ title }}</p>
      <p v-if="description" class="text-muted-foreground text-sm leading-6">{{ description }}</p>
    </div>
    <slot />
  </div>
</template>
