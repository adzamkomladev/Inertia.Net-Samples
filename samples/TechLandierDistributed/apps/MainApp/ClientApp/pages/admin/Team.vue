<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Head, useForm, usePage } from '@inertiajs/vue3'
import { useClipboard } from '@vueuse/core'
import { Check, Copy, Eye, EyeOff, Loader2, Mail, ShieldCheck, UserMinus, UserPlus, Wand2 } from '@lucide/vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import FieldError from '@/components/FieldError.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

type Member = { id: string; name: string; email: string; role: string }
const props = defineProps<{ members: Member[] }>()

const page = usePage()
const ownId = computed(() => page.props.auth.user?.id)
// The signed-in admin first, then everyone else alphabetically.
const sorted = computed(() => [...props.members].sort((a, b) => Number(b.id === ownId.value) - Number(a.id === ownId.value) || a.name.localeCompare(b.name)))
const initials = (name: string) => name.split(' ').map((p) => p[0]).join('').slice(0, 2).toUpperCase()

// ---------------------------------------------------------------- add

const adding = ref(false)
const showPassword = ref(false)
const form = useForm({ name: '', email: '', password: '' })
const { copy, copied } = useClipboard({ legacy: true })

watch(adding, (open) => {
  if (!open) return
  form.reset()
  form.clearErrors()
  showPassword.value = false
})

/** 16 characters from an unambiguous alphabet, drawn with the platform CSPRNG. */
function generatePassword() {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%'
  form.password = Array.from(crypto.getRandomValues(new Uint32Array(16)), (n) => alphabet[n % alphabet.length]).join('')
  showPassword.value = true
}
const strength = computed(() => {
  const p = form.password
  const variety = [/[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z0-9]/].filter((r) => r.test(p)).length
  return p.length < 12 ? 0 : variety >= 3 && p.length >= 14 ? 3 : variety >= 2 ? 2 : 1
})

function create() {
  form.post('/admin/team', { preserveScroll: true, onSuccess: () => (adding.value = false) })
}

// ---------------------------------------------------------------- remove

const remove = useForm({})
const removing = ref<Member | null>(null)
function removeAccess() {
  if (!removing.value) return
  remove.post(`/admin/team/${removing.value.id}/remove`, { preserveScroll: true, onSuccess: () => (removing.value = null) })
}
</script>

<template>
  <Head title="Team" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Team" description="Administrators have full console access: orders, refunds, catalog, settings and this page.">
      <template #actions>
        <Button @click="adding = true"><UserPlus /> Add administrator</Button>
      </template>
    </PageHeader>

    <section class="surface overflow-hidden">
      <header class="flex items-center justify-between border-b px-5 py-3">
        <h2 class="text-sm font-medium">Administrators <span class="text-muted-foreground tabular-nums">· {{ members.length }}</span></h2>
        <p class="text-muted-foreground hidden text-xs sm:block">Removing access keeps their customer account and orders.</p>
      </header>
      <TransitionGroup tag="ul" class="divide-y" enter-active-class="transition duration-300" enter-from-class="opacity-0 -translate-y-1" leave-active-class="transition duration-200" leave-to-class="opacity-0">
        <li v-for="m in sorted" :key="m.id" class="hover:bg-muted/30 flex items-center gap-4 px-5 py-4 transition-colors">
          <span class="bg-accent text-accent-foreground flex size-10 shrink-0 items-center justify-center rounded-full text-sm font-semibold">{{ initials(m.name) }}</span>
          <div class="min-w-0 flex-1">
            <p class="flex items-center gap-2 font-medium">
              <span class="truncate">{{ m.name }}</span>
              <span v-if="m.id === ownId" class="bg-muted text-muted-foreground rounded-full px-2 py-0.5 text-[11px] font-medium">You</span>
            </p>
            <a :href="`mailto:${m.email}`" class="text-muted-foreground hover:text-foreground flex items-center gap-1.5 truncate text-sm"><Mail class="size-3.5 shrink-0" aria-hidden="true" />{{ m.email }}</a>
          </div>
          <span class="text-muted-foreground hidden items-center gap-1.5 text-xs sm:flex"><ShieldCheck class="text-primary size-4" aria-hidden="true" /> Full access</span>
          <Button v-if="m.id !== ownId" variant="ghost" size="sm" class="text-muted-foreground hover:text-destructive" @click="removing = m">
            <UserMinus /> <span class="sr-only sm:not-sr-only">Remove</span>
          </Button>
        </li>
      </TransitionGroup>
    </section>
  </div>

  <Dialog v-model:open="adding">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>Add an administrator</DialogTitle>
        <DialogDescription>They can sign in right away. Share the password privately and ask them to change it.</DialogDescription>
      </DialogHeader>
      <form id="team-form" class="space-y-4" novalidate @submit.prevent="create">
        <div class="space-y-2">
          <Label for="team-name">Full name</Label>
          <Input id="team-name" v-model="form.name" maxlength="80" autocomplete="off" :aria-invalid="!!form.errors.name" />
          <FieldError :message="form.errors.name" />
        </div>
        <div class="space-y-2">
          <Label for="team-email">Work email</Label>
          <Input id="team-email" v-model="form.email" type="email" maxlength="254" autocomplete="off" :aria-invalid="!!form.errors.email" />
          <FieldError :message="form.errors.email" />
        </div>
        <div class="space-y-2">
          <div class="flex items-center justify-between">
            <Label for="team-password">Initial password</Label>
            <button type="button" class="text-primary flex items-center gap-1 text-xs font-medium hover:underline" @click="generatePassword"><Wand2 class="size-3.5" aria-hidden="true" /> Generate</button>
          </div>
          <div class="relative">
            <Input
              id="team-password" v-model="form.password" :type="showPassword ? 'text' : 'password'" maxlength="128"
              autocomplete="new-password" class="pr-20 font-mono" :aria-invalid="!!form.errors.password" aria-describedby="password-strength"
            />
            <div class="absolute inset-y-0 right-1 flex items-center">
              <Button type="button" size="icon-xs" variant="ghost" :aria-label="copied ? 'Copied' : 'Copy password'" :disabled="!form.password" @click="copy(form.password)">
                <Check v-if="copied" class="text-success animate-pop" /><Copy v-else />
              </Button>
              <Button type="button" size="icon-xs" variant="ghost" :aria-label="showPassword ? 'Hide password' : 'Show password'" :aria-pressed="showPassword" @click="showPassword = !showPassword">
                <EyeOff v-if="showPassword" /><Eye v-else />
              </Button>
            </div>
          </div>
          <div id="password-strength" class="flex items-center gap-2" aria-live="polite">
            <div class="flex flex-1 gap-1" aria-hidden="true">
              <span
                v-for="i in 3" :key="i" class="h-1 flex-1 rounded-full transition-colors duration-300"
                :class="strength >= i ? (strength === 1 ? 'bg-warning' : 'bg-success') : 'bg-muted'"
              />
            </div>
            <span class="text-muted-foreground w-28 text-right text-xs">{{ ['At least 12 characters', 'Fair', 'Good', 'Strong'][strength] }}</span>
          </div>
          <FieldError :message="form.errors.password" />
        </div>
      </form>
      <DialogFooter>
        <Button type="button" variant="ghost" @click="adding = false">Cancel</Button>
        <Button type="submit" form="team-form" :disabled="form.processing">
          <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" /> Create administrator
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <ConfirmDialog
    :open="removing !== null"
    :title="`Remove ${removing?.name ?? 'this admin'}'s access?`"
    description="They keep their customer account and order history but lose the console immediately."
    confirm-label="Remove access"
    destructive
    :processing="remove.processing"
    @update:open="!$event && (removing = null)"
    @confirm="removeAccess"
  />
</template>
