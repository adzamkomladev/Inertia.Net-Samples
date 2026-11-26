<script setup lang="ts">
import { dismissToast, toasts } from '@/composables/useFlashToast';
</script>

<template>
    <div class="fixed top-4 right-4 z-50 flex flex-col gap-2">
        <TransitionGroup
            enter-active-class="transition duration-200 ease-out"
            enter-from-class="translate-x-4 opacity-0"
            enter-to-class="translate-x-0 opacity-100"
            leave-active-class="transition duration-150 ease-in"
            leave-from-class="translate-x-0 opacity-100"
            leave-to-class="translate-x-4 opacity-0"
        >
            <div
                v-for="toast in toasts"
                :key="toast.id"
                class="flex max-w-sm min-w-64 items-center gap-2 rounded-lg border px-4 py-3 text-sm shadow-lg"
                :class="{
                    'border-green-200 bg-green-50 text-green-800 dark:border-green-800 dark:bg-green-950 dark:text-green-200':
                        toast.type === 'success',
                    'border-red-200 bg-red-50 text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200':
                        toast.type === 'error',
                    'border-yellow-200 bg-yellow-50 text-yellow-800 dark:border-yellow-800 dark:bg-yellow-950 dark:text-yellow-200':
                        toast.type === 'warning',
                }"
            >
                <span class="flex-1">{{ toast.message }}</span>
                <button
                    class="shrink-0 opacity-50 hover:opacity-100"
                    @click="dismissToast(toast.id)"
                >
                    &times;
                </button>
            </div>
        </TransitionGroup>
    </div>
</template>
