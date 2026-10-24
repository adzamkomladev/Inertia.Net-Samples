<script lang="ts">
  import { router, usePage } from '@inertiajs/svelte'
  import Check from '@lucide/svelte/icons/check'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import MailCheck from '@lucide/svelte/icons/mail-check'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import type { AuthUser } from '@/types'

  /** Resend verification links; the confirm link itself is handled by the backend. */
  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  let sending = $state(false)
  let sent = $state(false)

  const resend = () =>
    router.post('/email/verification-notification', {}, {
      onStart: () => (sending = true),
      onSuccess: () => (sent = true),
      onFinish: () => (sending = false),
    })
  const logout = () => router.post('/logout')
</script>

<PageTitle title="Verify your e-mail" />
<span class="bg-cta/15 text-cta-foreground mb-4 flex size-12 items-center justify-center rounded-2xl">
  <MailCheck class="size-6" strokeWidth={1.75} aria-hidden="true" />
</span>
<h1 class="font-display text-2xl font-semibold tracking-tight">Check your inbox</h1>
<p class="text-muted-foreground mt-1 text-sm">
  We sent a verification link to <span class="text-foreground font-medium">{user?.email}</span>. Open it to start
  booking windows on any CarParker deck.
</p>
<p class="text-muted-foreground mt-4 text-sm">Nothing arrived? Check your spam folder or request a new link.</p>
<Button size="lg" class="mt-8 w-full" disabled={sending} onclick={resend}>
  {#if sending}<Loader2 class="animate-spin" aria-hidden="true" />
  {:else if sent}<Check aria-hidden="true" />{/if}
  {sending ? 'Sending…' : sent ? 'Sent. Send again' : 'Resend verification e-mail'}
</Button>
<p class="text-muted-foreground mt-6 text-center text-sm">
  Wrong account?
  <button
    type="button"
    class="text-brand focus-visible:ring-ring rounded font-medium underline-offset-4 outline-none hover:underline focus-visible:ring-2"
    onclick={logout}
  >Log out</button>
</p>
