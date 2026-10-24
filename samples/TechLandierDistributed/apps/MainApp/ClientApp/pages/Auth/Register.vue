<script setup lang="ts">
import { Head, Link, useForm } from '@inertiajs/vue3'
import { Loader2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

type Field = 'name' | 'email' | 'password' | 'passwordConfirmation'

const fields: { name: Field; label: string; type: string; autocomplete: string }[] = [
  { name: 'name', label: 'Name', type: 'text', autocomplete: 'name' },
  { name: 'email', label: 'E-mail', type: 'email', autocomplete: 'email' },
  { name: 'password', label: 'Password', type: 'password', autocomplete: 'new-password' },
  { name: 'passwordConfirmation', label: 'Confirm password', type: 'password', autocomplete: 'new-password' },
]

const form = useForm('post', '/register', { name: '', email: '', password: '', passwordConfirmation: '' })

const submit = () =>
  form.submit({ onError: () => form.reset('password', 'passwordConfirmation') })
</script>

<template>
  <Head title="Sign up" />
  <h1 class="text-2xl font-semibold tracking-tight">Create your account</h1>
  <p class="text-muted-foreground mt-1 text-sm">We will send a link to verify your e-mail.</p>
  <form novalidate class="mt-8 grid gap-4" @submit.prevent="submit">
    <div v-for="f in fields" :key="f.name" class="grid gap-2">
      <Label :for="f.name">{{ f.label }}</Label>
      <Input
        :id="f.name"
        v-model="form[f.name]"
        :type="f.type"
        :autocomplete="f.autocomplete"
        :autofocus="f.name === 'name'"
        required
        :aria-invalid="form.invalid(f.name)"
        :aria-describedby="form.errors[f.name] ? `${f.name}-error` : undefined"
        @change="form.validate(f.name)"
      />
      <p v-if="form.errors[f.name]" :id="`${f.name}-error`" role="alert" class="text-destructive text-sm">{{ form.errors[f.name] }}</p>
    </div>
    <Button type="submit" size="lg" class="mt-2 w-full" :disabled="form.processing">
      <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
      {{ form.processing ? 'Creating account…' : 'Sign up' }}
    </Button>
  </form>
  <p class="text-muted-foreground mt-6 text-center text-sm">
    Already have an account?
    <Link href="/login" class="text-primary font-medium underline-offset-4 hover:underline">Log in</Link>
  </p>
</template>
