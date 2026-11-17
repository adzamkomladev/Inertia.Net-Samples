<script lang="ts">
  import { Link, useForm } from '@inertiajs/svelte'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import MailCheck from '@lucide/svelte/icons/mail-check'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'

  /** POST /forgot-password always answers the same way (flash + back), so the page cannot reveal who has an account. */
  const form = useForm({ email: '' })
  let sentTo = $state<string | null>(null)

  const submit = () => {
    const email = form.email.trim()
    form.post('/forgot-password', { preserveScroll: true, onSuccess: () => { sentTo = email } })
  }
</script>

<PageTitle title="Reset your password" />
{#if sentTo}
  <div role="status">
    <span class="bg-primary/10 text-primary grid size-12 place-items-center rounded-xl"><MailCheck class="size-6" aria-hidden="true" /></span>
    <h1 class="font-display mt-5 text-2xl font-semibold tracking-tight">Check your email</h1>
    <p class="text-muted-foreground mt-2 text-sm">
      If <span class="text-foreground font-medium break-all">{sentTo}</span> has an account, a reset link is on its way. It works once, for 60 minutes.
    </p>
    <p class="text-muted-foreground mt-4 text-sm">Nothing after a few minutes? Check spam, or try again.</p>
    <div class="mt-8 grid gap-3">
      <Button variant="outline" size="lg" class="w-full" onclick={() => { sentTo = null; form.reset() }}>Use a different email</Button>
      <Button href="/login" size="lg" class="w-full">Back to log in</Button>
    </div>
  </div>
{:else}
  <h1 class="font-display text-2xl font-semibold tracking-tight">Reset your password</h1>
  <p class="text-muted-foreground mt-1 text-sm">Enter the email you sign in with and we will send you a link to choose a new password.</p>
  <form novalidate class="mt-8 grid gap-4" onsubmit={(event: SubmitEvent) => { event.preventDefault(); submit() }}>
    <div class="grid gap-2">
      <Label for="email">E-mail</Label>
      <Input
        id="email" bind:value={form.email} type="email" autocomplete="email" autofocus required
        aria-invalid={!!form.errors.email}
        aria-describedby={form.errors.email ? 'email-error' : undefined}
      />
      {#if form.errors.email}<p id="email-error" role="alert" class="text-destructive text-sm">{form.errors.email}</p>{/if}
    </div>
    <Button type="submit" size="lg" class="w-full" disabled={form.processing}>
      {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
      {form.processing ? 'Sending link…' : 'Send reset link'}
    </Button>
  </form>
  <p class="text-muted-foreground mt-6 text-center text-sm">
    Remembered it?
    <Link href="/login" class="text-brand font-medium underline-offset-4 hover:underline">Log in</Link>
  </p>
{/if}
