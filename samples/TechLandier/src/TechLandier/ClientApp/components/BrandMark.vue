<script setup lang="ts">
withDefaults(defineProps<{ size?: 'sm' | 'md' }>(), { size: 'md' })
</script>

<!-- Wordmark: type does the work, a single accent dot signs it. -->
<template>
  <span class="font-display inline-flex items-baseline font-semibold tracking-tight" :class="size === 'md' ? 'text-xl' : 'text-base'">
    TechLandier<span class="text-primary" aria-hidden="true">.</span>
  </span>
</template>
