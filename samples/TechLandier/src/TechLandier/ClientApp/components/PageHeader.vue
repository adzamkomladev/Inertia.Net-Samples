<script setup lang="ts">
defineProps<{ title?: string; description?: string }>()
</script>

<!-- Title block shared by every app page: title, optional description and right-aligned actions. -->
<template>
  <header class="flex flex-wrap items-end justify-between gap-x-4 gap-y-3">
    <div class="min-w-0 space-y-1">
      <h1 class="text-2xl font-semibold tracking-tight text-balance"><slot name="title">{{ title }}</slot></h1>
      <p v-if="description || $slots.description" class="text-muted-foreground text-[15px]"><slot name="description">{{ description }}</slot></p>
    </div>
    <div v-if="$slots.actions" class="flex shrink-0 items-center gap-2"><slot name="actions" /></div>
  </header>
</template>
