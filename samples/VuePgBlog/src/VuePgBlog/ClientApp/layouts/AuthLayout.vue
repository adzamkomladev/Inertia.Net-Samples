<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useTheme } from '@/composables/useTheme'

const page = usePage()
useFlashToast()
useTheme()
</script>

<template>
  <div class="bg-background grid min-h-screen lg:grid-cols-2">
    <div class="flex flex-col p-6 sm:p-8">
      <Link
        href="/posts"
        class="focus-visible:ring-ring flex w-fit items-center gap-2 rounded-lg text-sm font-semibold tracking-tight outline-none focus-visible:ring-2"
      >
        <span class="bg-foreground text-background flex size-8 items-center justify-center rounded-lg font-serif text-lg leading-none font-semibold" aria-hidden="true">{{ page.props.appName.charAt(0) }}</span>
        {{ page.props.appName }}
      </Link>
      <main class="animate-rise mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">
        <slot />
      </main>
      <p class="text-muted-foreground text-center text-xs">&copy; {{ new Date().getFullYear() }} {{ page.props.appName }}</p>
    </div>
    <aside class="bg-muted hidden items-center p-16 lg:flex">
      <blockquote class="max-w-md font-serif text-3xl leading-snug tracking-tight">
        Write the thing you wish you had been able to read.
      </blockquote>
    </aside>
    <Toaster position="bottom-right" close-button />
  </div>
</template>
