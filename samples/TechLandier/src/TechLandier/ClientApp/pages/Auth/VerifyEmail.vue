<script setup lang="ts">
import { Head, router, usePage } from '@inertiajs/vue3'
import { Check, Loader2, MailCheck } from '@lucide/vue'
import { ref } from 'vue'
import { Button } from '@/components/ui/button'

const page = usePage()
const sending = ref(false)
const sent = ref(false)

const resend = () =>
  router.post('/email/verification-notification', {}, {
    onStart: () => (sending.value = true),
    onSuccess: () => (sent.value = true),
    onFinish: () => (sending.value = false),
  })
const logout = () => router.post('/logout')
</script>

<template>
  <Head title="Verify your e-mail" />
  <span class="bg-muted text-muted-foreground mb-4 flex size-11 items-center justify-center rounded-xl">
    <MailCheck class="size-5" :stroke-width="1.75" aria-hidden="true" />
  </span>
  <h1 class="text-2xl font-semibold tracking-tight">Check your inbox</h1>
  <p class="text-muted-foreground mt-1 text-sm">
    We sent a verification link to <span class="text-foreground font-medium">{{ page.props.auth.user?.email }}</span>. Open it to start
    posting, commenting and liking.
  </p>
  <p class="text-muted-foreground mt-4 text-sm">Nothing arrived? Check your spam folder or request a new link.</p>
  <Button class="mt-8 h-10 w-full rounded-lg active:scale-[0.98]" :disabled="sending" @click="resend">
    <Loader2 v-if="sending" class="animate-spin" aria-hidden="true" />
    <Check v-else-if="sent" aria-hidden="true" />
    {{ sending ? 'Sending…' : sent ? 'Sent. Send again' : 'Resend verification e-mail' }}
  </Button>
  <p class="text-muted-foreground mt-6 text-center text-sm">
    Wrong account?
    <button
      type="button"
      class="text-brand focus-visible:ring-ring rounded font-medium underline-offset-4 outline-none hover:underline focus-visible:ring-2"
      @click="logout"
    >Log out</button>
  </p>
</template>
