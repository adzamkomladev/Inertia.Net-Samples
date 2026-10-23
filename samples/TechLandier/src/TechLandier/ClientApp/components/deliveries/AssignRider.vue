<script setup lang="ts">
import { computed, ref } from 'vue'
import { useForm } from '@inertiajs/vue3'
import { Check, Loader2, UserPlus, UserRoundCog } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import RiderLoad from '@/components/deliveries/RiderLoad.vue'
import type { DeliveryRow, RiderAdminRow } from '@/types'

const props = defineProps<{ delivery: DeliveryRow; riders: RiderAdminRow[] }>()

const open = ref(false)
const form = useForm({ riderId: '' })

// Least-loaded riders first so the obvious pick sits at the top of the list.
const available = computed(() =>
  props.riders
    .filter((r) => r.isActive)
    .sort((a, b) => a.activeRuns / a.capacity - b.activeRuns / b.capacity || a.name.localeCompare(b.name)),
)

function assign(rider: RiderAdminRow) {
  // Re-picking the current rider is a no-op, except on a failed run where it starts the next attempt.
  if (rider.id === props.delivery.riderId && props.delivery.status !== 'failed') return (open.value = false)
  form.riderId = rider.id
  form.post(`/admin/deliveries/${props.delivery.id}/assign`, { preserveScroll: true, onSuccess: () => (open.value = false) })
}
</script>

<!-- Rider picker: searchable, sorted by spare capacity, assigns on select with an inline pending state. -->
<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        v-if="!delivery.riderId || delivery.status === 'failed'" size="sm"
        :aria-label="`${delivery.status === 'failed' ? 'Reassign' : 'Assign'} a rider for ${delivery.orderNumber}`"
      >
        <UserPlus /> {{ delivery.status === 'failed' ? 'Reassign' : 'Assign rider' }}
      </Button>
      <button
        v-else type="button"
        class="hover:bg-muted group -mx-2 flex min-h-10 items-center gap-2 rounded-lg px-2 text-left text-sm transition-colors md:min-h-8"
        :aria-label="`Change rider for ${delivery.orderNumber} (currently ${delivery.rider})`"
      >
        <span class="bg-accent text-accent-foreground flex size-7 shrink-0 items-center justify-center rounded-full text-[11px] font-semibold">
          {{ delivery.rider?.split(' ').map((p) => p[0]).join('').slice(0, 2) }}
        </span>
        <span class="truncate">{{ delivery.rider }}</span>
        <UserRoundCog class="text-muted-foreground size-3.5 opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100" aria-hidden="true" />
      </button>
    </PopoverTrigger>
    <PopoverContent class="w-80 p-0" align="start">
      <Command>
        <CommandInput placeholder="Search riders…" />
        <CommandList class="max-h-72">
          <CommandEmpty>No active rider matches.</CommandEmpty>
          <CommandGroup heading="Available riders">
            <CommandItem
              v-for="r in available" :key="r.id" :value="`${r.name} ${r.vehicle ?? ''} ${r.service}`"
              :disabled="form.processing" class="gap-3 py-2" @select="assign(r)"
            >
              <div class="min-w-0 flex-1">
                <p class="flex items-center gap-1.5 truncate font-medium">
                  {{ r.name }}
                  <Check v-if="r.id === delivery.riderId" class="text-primary size-3.5" aria-label="current rider" />
                </p>
                <p class="text-muted-foreground truncate text-xs">{{ r.vehicle ?? r.service.replace('_', ' ') }}</p>
              </div>
              <Loader2 v-if="form.processing && form.riderId === r.id" class="size-4 animate-spin" aria-label="Assigning" />
              <RiderLoad v-else :active="r.activeRuns" :capacity="r.capacity" compact />
            </CommandItem>
          </CommandGroup>
        </CommandList>
      </Command>
      <p v-if="form.errors.riderId" role="alert" class="text-destructive border-t px-3 py-2 text-xs">{{ form.errors.riderId }}</p>
    </PopoverContent>
  </Popover>
</template>
