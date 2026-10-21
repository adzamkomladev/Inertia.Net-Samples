<script lang="ts">
  import { Link, useForm } from '@inertiajs/svelte'
  import { Loader2 } from '@lucide/svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'

  /** POST /register with precognition validation; password confirmation checked server-side. */
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

<PageTitle title="Sign up" />
<h1 class="font-display text-2xl font-semibold tracking-tight">Create your account</h1>
<p class="text-muted-foreground mt-1 text-sm">We will send a link to verify your e-mail before your first booking.</p>
<form novalidate class="mt-8 grid gap-4" onsubmit={(event: SubmitEvent) => { event.preventDefault(); submit() }}>
  {#each fields as f (f.name)}
    <div class="grid gap-2">
      <Label for={f.name}>{f.label}</Label>
      <Input
        id={f.name} bind:value={form[f.name]} type={f.type} autocomplete={f.autocomplete} autofocus={f.name === 'name'} required
        aria-invalid={form.invalid(f.name)}
        aria-describedby={form.errors[f.name] ? `${f.name}-error` : undefined}
        onchange={() => form.validate(f.name)}
      />
      {#if form.errors[f.name]}<p id={`${f.name}-error`} role="alert" class="text-destructive text-sm">{form.errors[f.name]}</p>{/if}
    </div>
  {/each}
  <Button type="submit" size="lg" class="mt-2 w-full rounded-2xl" disabled={form.processing}>
    {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
    {form.processing ? 'Creating account…' : 'Sign up'}
  </Button>
</form>
<p class="text-muted-foreground mt-6 text-center text-sm">
  Already have an account?
  <Link href="/login" class="text-brand font-medium underline-offset-4 hover:underline">Log in</Link>
</p>
