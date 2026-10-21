<script setup lang="ts">
import { computed } from 'vue'
import { Link, usePage } from '@inertiajs/vue3'
import { Headphones, ShieldCheck, Truck, Zap } from '@lucide/vue'
import { Toaster } from '@/components/ui/sonner'
import 'vue-sonner/style.css'
import { useFlashToast } from '@/composables/useFlashToast'
import { useTheme } from '@/composables/useTheme'

const page = usePage()

useFlashToast()
useTheme()

const perks = [
  { icon: Zap, text: 'Split any order into 3, 6 or 12 installments.' },
  { icon: ShieldCheck, text: 'Warranties and device insurance on every flagship.' },
  { icon: Truck, text: 'Same-day rider delivery in supported zones.' },
  { icon: Headphones, text: 'Humans on support within 72 hours, guaranteed.' },
]
const year = computed(() => new Date().getFullYear())
</script>

<template>
  <div class="bg-background grid min-h-screen lg:grid-cols-2">
    <div class="flex flex-col p-6 sm:p-8">
      <Link
        href="/"
        class="focus-visible:ring-ring flex w-fit items-center gap-2.5 rounded-xl text-sm font-semibold tracking-tight outline-none focus-visible:ring-2"
      >
        <span class="from-brand to-primary text-brand-foreground flex size-8 items-center justify-center rounded-lg bg-gradient-to-br" aria-hidden="true">
          <Zap class="size-4" />
        </span>
        <span class="font-display text-base font-bold">{{ page.props.appName }}</span>
      </Link>
      <main class="animate-rise mx-auto flex w-full max-w-sm flex-1 flex-col justify-center py-10">
        <slot />
      </main>
      <p class="text-muted-foreground text-center text-xs">&copy; {{ year }} {{ page.props.appName }}</p>
    </div>
    <aside class="hero-gradient relative hidden items-center p-16 lg:flex">
      <div class="relative z-10 max-w-md space-y-8 text-white">
        <p class="font-display text-4xl leading-tight font-bold tracking-tight text-balance">
          Premium tech, <span class="text-lime-300">on your schedule.</span>
        </p>
        <ul class="space-y-4">
          <li v-for="perk in perks" :key="perk.text" class="flex items-start gap-3 text-sm text-white/85">
            <span class="flex size-8 shrink-0 items-center justify-center rounded-lg bg-white/10" aria-hidden="true">
              <component :is="perk.icon" class="size-4" />
            </span>
            {{ perk.text }}
          </li>
        </ul>
      </div>
      <div class="absolute inset-0 bg-[radial-gradient(80%_60%_at_70%_20%,rgba(255,255,255,0.12),transparent)]" aria-hidden="true" />
    </aside>
    <Toaster position="bottom-right" close-button />
  </div>
</template>
