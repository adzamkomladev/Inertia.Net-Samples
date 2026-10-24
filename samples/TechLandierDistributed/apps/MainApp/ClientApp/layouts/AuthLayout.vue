<script setup lang="ts">
import { Link } from '@inertiajs/vue3'
import BrandMark from '@/components/BrandMark.vue'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useTheme } from '@/composables/useTheme'

useFlashToast()
useTheme()

const points = [
  'Installments of 3, 6 or 12 payments on eligible orders',
  'Warranty registered automatically on delivery',
  'Card, mobile money or pay on delivery',
]
</script>

<template>
  <div class="bg-background grid min-h-screen lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
    <div class="flex flex-col p-6 sm:p-8">
      <Link href="/" class="w-fit" aria-label="TechLandier home"><BrandMark /></Link>
      <main class="mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">
        <slot />
      </main>
    </div>
    <aside class="bg-card hidden items-center p-16 lg:flex">
      <div class="max-w-md space-y-6">
        <p class="text-4xl font-semibold tracking-tight text-balance">Electronics you can pay for your way.</p>
        <ul class="text-muted-foreground space-y-3 text-[15px]">
          <li v-for="p in points" :key="p" class="flex gap-3"><span class="bg-primary mt-2.5 size-1.5 shrink-0 rounded-full" aria-hidden="true" />{{ p }}</li>
        </ul>
      </div>
    </aside>
    <Toaster position="bottom-right" close-button />
  </div>
</template>
