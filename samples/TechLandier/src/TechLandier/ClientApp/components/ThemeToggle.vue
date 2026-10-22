<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Check, Monitor, Moon, Sun } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { applyTheme } from '@/composables/useTheme'
import type { Theme } from '@/types'

const options = [
  { theme: 'light', icon: Sun, label: 'Light' },
  { theme: 'dark', icon: Moon, label: 'Dark' },
  { theme: 'system', icon: Monitor, label: 'System' },
] as const

const current = ref<Theme>('system')
// Read after mount so the server-rendered markup (always "system") hydrates cleanly.
onMounted(() => {
  const m = document.cookie.match(/(?:^|;\s*)theme=(light|dark|system)/)
  current.value = (m?.[1] as Theme) ?? 'system'
})
const set = (t: Theme) => {
  current.value = t
  applyTheme(t)
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon" aria-label="Change theme">
        <Sun v-if="current === 'light'" class="size-[18px]" />
        <Moon v-else-if="current === 'dark'" class="size-[18px]" />
        <Monitor v-else class="size-[18px]" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-36">
      <DropdownMenuItem v-for="o in options" :key="o.theme" @select="set(o.theme)">
        <component :is="o.icon" class="size-4" /> {{ o.label }}
        <Check v-if="current === o.theme" class="ml-auto size-3.5" />
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
