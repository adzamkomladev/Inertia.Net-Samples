<script setup lang="ts">
import SourceLinks from '@/components/SourceLinks.vue';

defineProps<{
    title: string;
    docs?: string;
    controller?: string;
}>();
</script>

<template>
    <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
        <p class="text-muted-foreground">
            <slot />
        </p>
        <SourceLinks :docs="docs" :controller="controller" />
    </div>
</template>
