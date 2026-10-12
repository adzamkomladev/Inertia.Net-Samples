<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3'
import { computed } from 'vue'
import { Button } from '@/components/ui/button'

const props = defineProps<{ status: number }>()

const messages: Record<number, { title: string; description: string }> = {
  403: { title: 'Forbidden', description: "You don't have permission to do that." },
  404: { title: 'Page not found', description: "The page you're looking for doesn't exist or was removed." },
  419: { title: 'Page expired', description: 'Your session timed out. Refresh the page and try again.' },
  429: { title: 'Too many requests', description: 'Slow down a little and try again in a moment.' },
  500: { title: 'Something went wrong', description: 'An unexpected error occurred. Please try again.' },
  503: { title: 'Service unavailable', description: "We're down for maintenance. Please check back soon." },
}
const error = computed(() => messages[props.status] ?? { title: 'Unexpected error', description: 'An unexpected error occurred. Please try again.' })
const goBack = () => window.history.back()
</script>

<template>
  <Head :title="error.title" />
  <div class="flex flex-col items-center py-20 text-center">
    <p class="text-muted-foreground/60 font-serif text-7xl tabular-nums">{{ status }}</p>
    <h1 class="mt-4 text-2xl font-semibold tracking-tight">{{ error.title }}</h1>
    <p class="text-muted-foreground mt-2 max-w-sm text-sm">{{ error.description }}</p>
    <div class="mt-8 flex gap-2">
      <Button as-child class="rounded-lg"><Link href="/posts">Back to home</Link></Button>
      <Button variant="ghost" class="rounded-lg" @click="goBack">Go back</Button>
    </div>
  </div>
</template>
