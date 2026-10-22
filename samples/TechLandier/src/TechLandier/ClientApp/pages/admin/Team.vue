<script setup lang="ts">
import { computed, ref } from 'vue'
import { Head, useForm, usePage } from '@inertiajs/vue3'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import PageHeader from '@/components/PageHeader.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
const props = defineProps<{
  members: { id: string; name: string; email: string; role: string }[]
}>()
const page = usePage()
const ownId = computed(() => page.props.auth.user?.id)
const form = useForm({ name: '', email: '', password: '' })
const remove = useForm({})
const removing = ref<string | null>(null)
function removeAccess() {
  if (removing.value)
    remove.post(`/admin/team/${removing.value}/remove`, {
      preserveScroll: true,
      onSuccess: () => {
        removing.value = null
      },
    })
}
</script>
<template>
  <Head title="Team management" />
  <ConfirmDialog
    :open="removing !== null"
    title="Remove admin access?"
    description="Their customer account and orders will be kept."
    confirm-label="Remove access"
    destructive
    :processing="remove.processing"
    @update:open="!$event && (removing = null)"
    @confirm="removeAccess"
  />
  <div class="space-y-6">
    <PageHeader
      title="Team management"
      description="Create administrator accounts and manage who can operate the store. Administrators have full console access."
    />
    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
      <section class="surface p-6">
        <h2 class="mb-4 font-semibold">
          Administrators ({{ props.members.length }})
        </h2>
        <ul class="divide-y">
          <li
            v-for="member in members"
            :key="member.id"
            class="flex flex-wrap items-center gap-3 py-4"
          >
            <div class="min-w-0 flex-1">
              <p class="font-medium">
                {{ member.name }}
                <span
                  v-if="member.id === ownId"
                  class="text-xs text-muted-foreground"
                  >(you)</span
                >
              </p>
              <p class="break-all text-sm text-muted-foreground">
                {{ member.email }}
              </p>
            </div>
            <Button
              v-if="member.id !== ownId"
              variant="outline"
              size="sm"
              :disabled="remove.processing"
              @click="removing = member.id"
              >Remove access</Button
            >
          </li>
        </ul>
      </section>
      <form
        class="surface space-y-4 p-6"
        @submit.prevent="
          form.post('/admin/team', {
            preserveScroll: true,
            onSuccess: () => form.reset(),
          })
        "
      >
        <h2 class="font-semibold">Add administrator</h2>
        <div class="space-y-2">
          <Label for="team-name">Name</Label
          ><Input
            id="team-name"
            v-model="form.name"
            required
            minlength="2"
            maxlength="80"
            autocomplete="name"
          />
        </div>
        <div class="space-y-2">
          <Label for="team-email">Email</Label
          ><Input
            id="team-email"
            v-model="form.email"
            type="email"
            required
            maxlength="254"
            autocomplete="email"
          />
        </div>
        <div class="space-y-2">
          <Label for="team-password">Initial password</Label
          ><Input
            id="team-password"
            v-model="form.password"
            type="password"
            required
            minlength="12"
            maxlength="128"
            autocomplete="new-password"
          />
        </div>
        <p
          v-for="(error, field) in form.errors"
          :key="field"
          class="text-sm text-destructive"
          role="alert"
        >
          {{ error }}
        </p>
        <Button type="submit" :disabled="form.processing"
          >Create administrator</Button
        >
      </form>
    </div>
  </div>
</template>
