<script setup lang="ts">
import { ref } from 'vue'
import { Head, useForm } from '@inertiajs/vue3'
import { Check, ChevronsUpDown, Clock, CopyCheck, Globe, Loader2, Store, Truck, Undo2 } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import FieldError from '@/components/FieldError.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import type { StoreSettings } from '@/types/management'

const props = defineProps<{ settings: StoreSettings; timeZones: string[] }>()

const form = useForm({ ...props.settings, hours: props.settings.hours.map((h) => ({ ...h })) })

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']
const TIMES = Array.from({ length: 48 }, (_, i) => `${String(Math.floor(i / 2)).padStart(2, '0')}:${i % 2 ? '30' : '00'}`)

const zoneOpen = ref(false)
function pickZone(zone: string) {
  form.timeZoneId = zone
  zoneOpen.value = false
}

/** Copies Monday's store and delivery hours to Tuesday–Friday. */
const copied = ref(false)
function copyMondayToWeekdays() {
  const monday = form.hours.find((h) => h.day === 1)
  if (!monday) return
  for (const h of form.hours) if (h.day >= 2 && h.day <= 5) Object.assign(h, { ...monday, day: h.day })
  copied.value = true
  setTimeout(() => (copied.value = false), 1600)
}

function save() {
  form.post('/admin/settings', { preserveScroll: true, onSuccess: () => form.defaults() })
}
</script>

<template>
  <Head title="Store settings" />

  <form class="flex flex-col gap-6 pb-16" novalidate @submit.prevent="save">
    <PageHeader title="Store settings" description="Contact details, opening hours and delivery windows customers see on the store and at checkout." />

    <section class="surface grid gap-x-8 gap-y-6 p-5 sm:p-6 lg:grid-cols-[16rem_1fr]">
      <div class="space-y-1">
        <h2 class="flex items-center gap-2 font-semibold"><Store class="size-4" aria-hidden="true" /> Store profile</h2>
        <p class="text-muted-foreground text-sm">Shown in the footer, emails and receipts.</p>
      </div>
      <div class="grid gap-5 sm:grid-cols-2">
        <div class="space-y-2">
          <Label for="store-name">Store name</Label>
          <Input id="store-name" v-model="form.name" maxlength="80" :aria-invalid="!!form.errors.name" />
          <FieldError :message="form.errors.name" />
        </div>
        <div class="space-y-2">
          <Label for="store-email">Support email</Label>
          <Input id="store-email" v-model="form.email" type="email" maxlength="254" :aria-invalid="!!form.errors.email" />
          <FieldError :message="form.errors.email" />
        </div>
        <div class="space-y-2">
          <Label for="store-phone">Phone</Label>
          <Input id="store-phone" v-model="form.phone" type="tel" inputmode="tel" maxlength="30" />
        </div>
        <div class="space-y-2">
          <Label for="store-address">Store address</Label>
          <Input id="store-address" v-model="form.address" maxlength="300" placeholder="Street, area, city" />
        </div>
      </div>
    </section>

    <section class="surface grid gap-x-8 gap-y-6 p-5 sm:p-6 lg:grid-cols-[16rem_1fr]">
      <div class="space-y-1">
        <h2 class="flex items-center gap-2 font-semibold"><Clock class="size-4" aria-hidden="true" /> Weekly schedule</h2>
        <p class="text-muted-foreground text-sm">Orders can be placed any time. Fulfilment and support follow these hours; windows end on the same day.</p>
      </div>

      <div class="min-w-0 space-y-4">
        <div class="flex flex-wrap items-end justify-between gap-3">
          <div class="w-full space-y-2 sm:w-72">
            <Label for="store-zone">Time zone</Label>
            <Popover v-model:open="zoneOpen">
              <PopoverTrigger as-child>
                <Button id="store-zone" type="button" variant="outline" role="combobox" :aria-expanded="zoneOpen" class="w-full justify-between rounded-md font-normal">
                  <span class="flex min-w-0 items-center gap-2"><Globe class="text-muted-foreground" aria-hidden="true" /><span class="truncate">{{ form.timeZoneId || 'Choose a time zone' }}</span></span>
                  <ChevronsUpDown class="text-muted-foreground" aria-hidden="true" />
                </Button>
              </PopoverTrigger>
              <PopoverContent class="w-72 p-0" align="start">
                <Command>
                  <CommandInput placeholder="Search city or region…" />
                  <CommandList class="max-h-64">
                    <CommandEmpty>No time zone found.</CommandEmpty>
                    <CommandGroup>
                      <CommandItem v-for="zone in timeZones" :key="zone" :value="zone" @select="pickZone(zone)">
                        <Check class="size-4" :class="zone === form.timeZoneId ? 'opacity-100' : 'opacity-0'" aria-hidden="true" />
                        {{ zone.replace(/_/g, ' ') }}
                      </CommandItem>
                    </CommandGroup>
                  </CommandList>
                </Command>
              </PopoverContent>
            </Popover>
            <FieldError :message="form.errors.timeZoneId" />
          </div>
          <Button type="button" variant="ghost" size="sm" @click="copyMondayToWeekdays">
            <component :is="copied ? Check : CopyCheck" :class="copied && 'text-success animate-pop'" aria-hidden="true" />
            {{ copied ? 'Copied to Tue–Fri' : 'Copy Monday to weekdays' }}
          </Button>
        </div>

        <div class="divide-y rounded-xl border">
          <div class="text-muted-foreground hidden grid-cols-[6.5rem_1fr_1fr] gap-4 px-4 py-2 text-xs font-medium md:grid">
            <span>Day</span>
            <span class="flex items-center gap-1.5"><Store class="size-3.5" aria-hidden="true" /> Store hours</span>
            <span class="flex items-center gap-1.5"><Truck class="size-3.5" aria-hidden="true" /> Delivery window</span>
          </div>
          <div v-for="h in form.hours" :key="h.day" class="grid gap-3 px-4 py-3 md:grid-cols-[6.5rem_1fr_1fr] md:items-center md:gap-4">
            <p class="font-medium">{{ DAYS[h.day] }}</p>

            <div v-for="kind in (['store', 'delivery'] as const)" :key="kind" class="flex min-h-10 items-center gap-3">
              <Switch
                :model-value="kind === 'store' ? !h.closed : !h.deliveryClosed"
                :aria-label="`${DAYS[h.day]} ${kind === 'store' ? 'store open' : 'deliveries available'}`"
                @update:model-value="(v: boolean) => kind === 'store' ? (h.closed = !v) : (h.deliveryClosed = !v)"
              />
              <span class="md:hidden text-muted-foreground w-16 text-xs">{{ kind === 'store' ? 'Store' : 'Delivery' }}</span>
              <Transition mode="out-in" enter-active-class="transition duration-200" enter-from-class="opacity-0" leave-active-class="transition duration-100" leave-to-class="opacity-0">
                <div v-if="kind === 'store' ? !h.closed : !h.deliveryClosed" class="flex items-center gap-1.5">
                  <Select v-model="h[kind === 'store' ? 'opens' : 'deliveryOpens']">
                    <SelectTrigger size="sm" class="w-24 tabular-nums" :aria-label="`${DAYS[h.day]} ${kind} from`"><SelectValue /></SelectTrigger>
                    <SelectContent class="max-h-64"><SelectItem v-for="t in TIMES" :key="t" :value="t">{{ t }}</SelectItem></SelectContent>
                  </Select>
                  <span class="text-muted-foreground text-xs">to</span>
                  <Select v-model="h[kind === 'store' ? 'closes' : 'deliveryCloses']">
                    <SelectTrigger size="sm" class="w-24 tabular-nums" :aria-label="`${DAYS[h.day]} ${kind} until`"><SelectValue /></SelectTrigger>
                    <SelectContent class="max-h-64"><SelectItem v-for="t in TIMES" :key="t" :value="t">{{ t }}</SelectItem></SelectContent>
                  </Select>
                </div>
                <span v-else class="text-muted-foreground text-sm">{{ kind === 'store' ? 'Closed' : 'No deliveries' }}</span>
              </Transition>
            </div>
          </div>
        </div>
        <FieldError :message="form.errors.hours" />
      </div>
    </section>

    <!-- Unsaved-changes bar: appears only when something changed, so saving is always one tap away. -->
    <Transition
      enter-active-class="transition duration-300 ease-(--ease-out-soft)" enter-from-class="translate-y-6 opacity-0"
      leave-active-class="transition duration-200" leave-to-class="translate-y-6 opacity-0"
    >
      <div v-if="form.isDirty || form.processing" class="fixed inset-x-0 bottom-4 z-40 flex justify-center px-4 lg:pl-64">
        <div class="bg-foreground text-background shadow-raised flex w-full max-w-md items-center gap-3 rounded-full py-2 pr-2 pl-5" role="status">
          <span class="flex-1 text-sm">Unsaved changes</span>
          <Button type="button" size="sm" variant="ghost" class="hover:bg-background/10 text-background hover:text-background" :disabled="form.processing" @click="form.reset()"><Undo2 /> Discard</Button>
          <Button type="submit" size="sm" variant="secondary" :disabled="form.processing">
            <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" /> Save changes
          </Button>
        </div>
      </div>
    </Transition>
  </form>
</template>
