<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{ icon?: Component; title: string; description?: string }>()
</script>

<!-- Empty state: says what is missing and what to do next (put the action in the default slot). -->
<template>
  <div class="flex flex-col items-center gap-4 px-6 py-14 text-center">
    <span v-if="icon" class="bg-muted text-muted-foreground flex size-12 items-center justify-center rounded-full">
      <component :is="icon" class="size-5" :stroke-width="1.75" aria-hidden="true" />
    </span>
    <div class="max-w-sm space-y-1.5">
      <p class="font-medium">{{ title }}</p>
      <p v-if="description" class="text-muted-foreground text-sm leading-6">{{ description }}</p>
    </div>
    <slot />
  </div>
</template>
