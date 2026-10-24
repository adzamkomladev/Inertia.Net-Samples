<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Volume2, VolumeX } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { soundEnabled, setSoundEnabled } from '@/lib/notification-sound'
const enabled = ref(false)
onMounted(() => { enabled.value = soundEnabled() })
async function toggle() {
  try { await setSoundEnabled(!enabled.value); enabled.value = soundEnabled() } catch { enabled.value = false }
}
</script>
<template><Button variant="ghost" size="icon" :aria-pressed="enabled" :aria-label="enabled ? 'Mute notification sounds' : 'Enable notification sounds'" :title="enabled ? 'Mute notification sounds' : 'Enable notification sounds'" @click="toggle"><Volume2 v-if="enabled" class="size-4" /><VolumeX v-else class="size-4" /></Button></template>
