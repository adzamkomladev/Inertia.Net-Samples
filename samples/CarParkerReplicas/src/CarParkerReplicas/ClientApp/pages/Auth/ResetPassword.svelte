<script lang="ts">
  import { Link, useForm } from '@inertiajs/svelte'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'

  /** GET /reset-password?token=…: only rendered for a live token. POST /reset-password redirects to /login on success;
   *  an expired or used token sends the visitor to /forgot-password with a toast. */
  /** minLength: 8 for customers, 12 for staff. */
  let { token, email, minLength = 8 }: { token: string; email: string; minLength?: number } = $props()

  // svelte-ignore state_referenced_locally
  const form = useForm({ token, password: '', passwordConfirmation: '' })

  const submit = () =>
    form.post('/reset-password', { onError: () => form.reset('password', 'passwordConfirmation') })
</script>

<PageTitle title="Choose a new password" />
<h1 class="font-display text-2xl font-semibold tracking-tight">Choose a new password</h1>
<p class="text-muted-foreground mt-1 text-sm">For <span class="text-foreground font-medium break-all">{email}</span>. Every other device signed in to this account is signed out.</p>
<form novalidate class="mt-8 grid gap-4" onsubmit={(event: SubmitEvent) => { event.preventDefault(); submit() }}>
  <!-- Lets password managers file the new password under the right account. -->
  <input type="email" name="username" autocomplete="username" value={email} readonly hidden />
  <div class="grid gap-2">
    <Label for="password">New password</Label>
    <Input
      id="password" bind:value={form.password} type="password" autocomplete="new-password" autofocus required minlength={minLength}
      aria-invalid={!!form.errors.password}
      aria-describedby={form.errors.password ? 'password-hint password-error' : 'password-hint'}
    />
    <p id="password-hint" class="text-muted-foreground text-sm">At least {minLength} characters.</p>
    {#if form.errors.password}<p id="password-error" role="alert" class="text-destructive text-sm">{form.errors.password}</p>{/if}
  </div>
  <div class="grid gap-2">
    <Label for="password-confirmation">Repeat new password</Label>
    <Input
      id="password-confirmation" bind:value={form.passwordConfirmation} type="password" autocomplete="new-password" required
      aria-invalid={!!form.errors.passwordConfirmation}
      aria-describedby={form.errors.passwordConfirmation ? 'confirmation-error' : undefined}
    />
    {#if form.errors.passwordConfirmation}<p id="confirmation-error" role="alert" class="text-destructive text-sm">{form.errors.passwordConfirmation}</p>{/if}
  </div>
  {#if form.errors.token}<p role="alert" class="text-destructive text-sm">{form.errors.token}</p>{/if}
  <Button type="submit" size="lg" class="w-full" disabled={form.processing}>
    {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
    {form.processing ? 'Saving…' : 'Save new password'}
  </Button>
</form>
<p class="text-muted-foreground mt-6 text-center text-sm">
  <Link href="/login" class="text-brand font-medium underline-offset-4 hover:underline">Back to log in</Link>
</p>
