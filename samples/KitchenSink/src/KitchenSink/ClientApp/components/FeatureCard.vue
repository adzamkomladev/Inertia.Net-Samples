<script lang="ts">
export default { inheritAttrs: false };
</script>

<script setup lang="ts">
import {
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
} from '@/components/ui/card';

defineProps<{
    title: string;
    description?: string;
    infoCard?: boolean;
    badge?: string;
}>();
</script>

<template>
    <Card v-bind="$attrs" :class="{ 'info-card': infoCard }">
        <CardHeader>
            <div
                v-if="$slots['header-action']"
                class="flex items-center justify-between"
            >
                <div>
                    <CardTitle class="flex items-center gap-2">
                        {{ title }}
                        <span
                            v-if="badge"
                            class="shrink-0 rounded bg-indigo-500/10 px-2 py-1 text-[10px] leading-none font-semibold text-indigo-500"
                            >{{ badge }}</span
                        >
                    </CardTitle>
                    <CardDescription v-if="description || $slots.description">
                        <slot name="description">{{ description }}</slot>
                    </CardDescription>
                </div>
                <slot name="header-action" />
            </div>
            <template v-else>
                <CardTitle class="flex items-center gap-2">
                    {{ title }}
                    <span
                        v-if="badge"
                        class="shrink-0 rounded bg-indigo-500/10 px-2 py-1 text-[10px] leading-none font-semibold text-indigo-500"
                        >{{ badge }}</span
                    >
                </CardTitle>
                <CardDescription v-if="description || $slots.description">
                    <slot name="description">{{ description }}</slot>
                </CardDescription>
            </template>
        </CardHeader>
        <CardContent>
            <slot />
        </CardContent>
    </Card>
</template>

<style>
[data-slot='card'].info-card {
    background-color: color-mix(in srgb, var(--muted) 50%, white);
    box-shadow: none;
    border-color: rgba(0, 0, 0, 0.04);
}

.dark [data-slot='card'].info-card {
    background-color: color-mix(in srgb, var(--muted) 50%, var(--background));
    border-color: rgba(255, 255, 255, 0.04);
}
</style>
