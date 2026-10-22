<script lang="ts">
  import { Link, useForm } from '@inertiajs/svelte'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'

  /** POST targets: /login (back+errors on failure, redirect on success). */
  let { returnUrl }: { returnUrl: string | null } = $props()

  // svelte-ignore state_referenced_locally
  const form = useForm({ email: '', password: '', remember: false, returnUrl })

  const submit = () =>
    form.post('/login', { onError: () => form.reset('password') })
</script>

<PageTitle title="Log in" />
<h1 class="font-display text-2xl font-semibold tracking-tight">Log in</h1>
<p class="text-muted-foreground mt-1 text-sm">Log in to book bays, manage visits and file claims.</p>
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
  <div class="grid gap-2">
    <Label for="password">Password</Label>
    <Input
      id="password" bind:value={form.password} type="password" autocomplete="current-password" required
      aria-invalid={!!form.errors.password}
      aria-describedby={form.errors.password ? 'password-error' : undefined}
    />
    {#if form.errors.password}<p id="password-error" role="alert" class="text-destructive text-sm">{form.errors.password}</p>{/if}
  </div>
  <div class="flex items-center gap-2">
    <Switch id="remember" bind:checked={form.remember} />
    <Label for="remember" class="font-normal">Remember me</Label>
  </div>
  <Button type="submit" size="lg" class="w-full" disabled={form.processing}>
    {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
    {form.processing ? 'Logging in…' : 'Log in'}
  </Button>
</form>
<p class="text-muted-foreground mt-6 text-center text-sm">
  New to CarParker?
  <Link href="/register" class="text-brand font-medium underline-offset-4 hover:underline">Create an account</Link>
</p>
