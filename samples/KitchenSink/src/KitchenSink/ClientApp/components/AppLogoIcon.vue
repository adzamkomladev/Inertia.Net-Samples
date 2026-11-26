<script setup lang="ts">
import type { HTMLAttributes } from 'vue';

defineOptions({
    inheritAttrs: false,
});

type Props = {
    className?: HTMLAttributes['class'];
};

defineProps<Props>();
</script>

<template>
    <svg
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 25 14"
        :class="className"
        v-bind="$attrs"
    >
        <path
            d="M7.05 0H0L6.81 6.81L0 13.62H7.05L13.86 6.81L7.05 0Z"
            fill="currentColor"
        />
        <path
            d="M17.7 0H10.65L17.46 6.81L10.65 13.62H17.7L24.51 6.81L17.7 0Z"
            fill="currentColor"
        />
    </svg>
</template>
