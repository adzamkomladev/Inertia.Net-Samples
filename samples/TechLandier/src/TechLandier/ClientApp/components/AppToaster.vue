<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'

// The one toaster for the whole app: mounted by app.ts outside Inertia's page tree, so it survives layout changes.
// On phones toasts sit at the top so they never cover the sticky purchase and navigation bars.
const phone = useMediaQuery('(max-width: 639px)')
</script>

<template>
  <Toaster :position="phone ? 'top-center' : 'bottom-right'" close-button />
</template>
