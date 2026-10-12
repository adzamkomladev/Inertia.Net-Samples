import { useForm } from '@inertiajs/react'
import { Check, Loader2 } from 'lucide-react'
import { useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
import { toast } from 'sonner'
import { cn } from 'cn'
import { PageHeader } from '@/components/PageHeader'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Switch } from '@/components/ui/switch'
import AppLayout, { initials } from '@/layouts/AppLayout'
import { uploadImage } from '@/lib/upload'
import type { SettingsPageProps } from '@/types/settings'
import type { Theme } from '@/types/shared'

const themes: { value: Theme; label: string }[] = [
  { value: 'system', label: 'System' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]

function Section({ title, description, children }: { title: string; description: string; children: ReactNode }) {
  return (
    <section className="grid gap-5 py-8 md:grid-cols-[200px_1fr] md:gap-10">
      <div className="space-y-1">
        <h2 className="text-base font-semibold">{title}</h2>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
      <div className="min-w-0 space-y-6">{children}</div>
    </section>
  )
}

function SaveButton({ processing, disabled, recentlySuccessful, label }: { processing: boolean; disabled?: boolean; recentlySuccessful: boolean; label: string }) {
  return (
    <div className="flex items-center gap-3">
      <Button type="submit" className="rounded-lg active:scale-[0.98]" disabled={processing || disabled}>
        {processing && <Loader2 className="animate-spin" aria-hidden />}
        {processing ? 'Saving…' : label}
      </Button>
      <span role="status" className="min-w-14 text-sm text-muted-foreground">
        {recentlySuccessful && (
          <span className="inline-flex animate-in items-center gap-1 fade-in">
            <Check className="size-4" aria-hidden /> Saved
          </span>
        )}
      </span>
    </div>
  )
}

function MockScreen({ tone }: { tone: 'light' | 'dark' }) {
  const dark = tone === 'dark'
  return (
    <div className={cn('flex h-full w-full gap-1 p-1.5', dark ? 'bg-neutral-900' : 'bg-white')} aria-hidden>
      <div className={cn('w-1/4 rounded-sm', dark ? 'bg-neutral-800' : 'bg-neutral-100')} />
      <div className="flex flex-1 flex-col gap-1 pt-0.5">
        <div className={cn('h-1.5 w-2/3 rounded-full', dark ? 'bg-neutral-200' : 'bg-neutral-800')} />
        <div className={cn('h-1 w-full rounded-full', dark ? 'bg-neutral-700' : 'bg-neutral-200')} />
        <div className={cn('h-1 w-4/5 rounded-full', dark ? 'bg-neutral-700' : 'bg-neutral-200')} />
        <div className="mt-auto h-1.5 w-1/3 rounded-full bg-brand" />
      </div>
    </div>
  )
}

function ThemePreview({ value }: { value: Theme }) {
  return (
    <div className="relative h-16 overflow-hidden rounded-md border" aria-hidden>
      <MockScreen tone={value === 'dark' ? 'dark' : 'light'} />
      {value === 'system' && (
        <div className="absolute inset-0 [clip-path:inset(0_0_0_50%)]">
          <MockScreen tone="dark" />
        </div>
      )}
    </div>
  )
}

function ProfileSection({ profile }: Pick<SettingsPageProps, 'profile'>) {
  const form = useForm({ displayName: profile.displayName, avatarUploadId: null as string | null, removeAvatar: false })
  const [preview, setPreview] = useState<string | null>(profile.avatarUrl)
  const [uploading, setUploading] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  async function onFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    try {
      const { id, url } = await uploadImage(file, 'avatar')
      form.setData((d) => ({ ...d, avatarUploadId: id, removeAvatar: false }))
      setPreview(url)
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Upload failed')
    } finally {
      setUploading(false)
    }
  }

  function removeAvatar() {
    form.setData((d) => ({ ...d, avatarUploadId: null, removeAvatar: true }))
    setPreview(null)
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    form.put('/settings/profile', { preserveScroll: true })
  }

  return (
    <form onSubmit={submit}>
      <Section title="Profile" description="How you appear on your posts and comments.">
        <div className="flex items-center gap-4">
          <Avatar className="size-16">
            {preview && <AvatarImage src={preview} alt="" />}
            <AvatarFallback className="text-lg">{initials(form.data.displayName)}</AvatarFallback>
          </Avatar>
          <div className="flex flex-wrap gap-2">
            <input ref={fileRef} type="file" accept="image/png,image/jpeg,image/gif,image/webp" className="hidden" onChange={onFile} />
            <Button type="button" variant="outline" size="sm" className="rounded-lg" disabled={uploading || form.processing} onClick={() => fileRef.current?.click()}>
              {uploading && <Loader2 className="animate-spin" aria-hidden />}
              {uploading ? 'Uploading…' : 'Change avatar'}
            </Button>
            {preview && (
              <Button
                type="button" variant="ghost" size="sm" disabled={uploading || form.processing} onClick={removeAvatar}
                className="text-destructive hover:bg-destructive/10 hover:text-destructive"
              >
                Remove
              </Button>
            )}
          </div>
        </div>
        {form.errors.avatarUploadId && <p role="alert" className="text-sm text-destructive">{form.errors.avatarUploadId}</p>}

        <div className="space-y-2">
          <Label htmlFor="displayName">Display name</Label>
          <Input
            id="displayName"
            value={form.data.displayName}
            maxLength={60}
            onChange={(e) => form.setData('displayName', e.target.value)}
            aria-invalid={!!form.errors.displayName}
          />
          {form.errors.displayName && <p role="alert" className="text-sm text-destructive">{form.errors.displayName}</p>}
        </div>

        <div className="space-y-2">
          <Label htmlFor="email">Email</Label>
          <Input id="email" value={profile.email} readOnly disabled />
        </div>

        <SaveButton processing={form.processing} disabled={uploading} recentlySuccessful={form.recentlySuccessful} label="Save profile" />
      </Section>
    </form>
  )
}

const toggles = [
  { key: 'emailOnComment', label: 'New comments', hint: 'Email me when someone comments on my post.' },
  { key: 'emailOnLike', label: 'New likes', hint: 'Email me when someone likes my post.' },
  { key: 'dailyRecommendationDigest', label: 'Daily recommendations', hint: 'Yesterday’s recommended posts you haven’t seen yet.' },
  { key: 'weeklyDigest', label: 'Weekly digest', hint: 'A weekly summary of views, likes and comments.' },
] as const

function PreferencesSections({ preferences }: Pick<SettingsPageProps, 'preferences'>) {
  const form = useForm({ ...preferences, dailyRecommendationDigest: preferences.dailyRecommendationDigest ?? false, timeZoneId: preferences.timeZoneId ?? 'UTC' })

  function submit(e: FormEvent) {
    e.preventDefault()
    form.put('/settings/preferences', { preserveScroll: true })
  }

  return (
    <form onSubmit={submit} className="divide-y">
      <Section title="Appearance" description="Choose how the app looks on this device.">
        <div className="space-y-3">
          <RadioGroup
            aria-label="Theme"
            value={form.data.theme}
            onValueChange={(v) => form.setData('theme', v as Theme)}
            className="grid-cols-3 gap-3"
          >
            {themes.map(({ value, label }) => (
              <Label
                key={value}
                htmlFor={`theme-${value}`}
                className="group/tile flex cursor-pointer flex-col items-stretch gap-2 rounded-lg border p-2 transition-colors hover:bg-muted/50 has-[[data-state=checked]]:border-foreground has-[[data-state=checked]]:ring-1 has-[[data-state=checked]]:ring-foreground has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50"
              >
                <RadioGroupItem id={`theme-${value}`} value={value} className="sr-only" />
                <ThemePreview value={value} />
                <span className="flex items-center justify-between px-0.5 text-sm font-medium">
                  {label}
                  <Check className="size-3.5 opacity-0 group-has-[[data-state=checked]]/tile:opacity-100" aria-hidden />
                </span>
              </Label>
            ))}
          </RadioGroup>
          {form.errors.theme && <p role="alert" className="text-sm text-destructive">{form.errors.theme}</p>}
        </div>
      </Section>

      <Section title="Notifications" description="Choose which emails you get.">
        <div className="divide-y">
          {toggles.map(({ key, label, hint }) => (
            <div key={key} className="flex items-center justify-between gap-4 py-3 first:pt-0">
              <div className="space-y-0.5">
                <Label htmlFor={key}>{label}</Label>
                <p id={`${key}-hint`} className="text-sm text-muted-foreground">{hint}</p>
              </div>
              <Switch id={key} checked={form.data[key]} onCheckedChange={(v) => form.setData(key, v)} aria-describedby={`${key}-hint`} />
            </div>
          ))}
        </div>

        <div className="space-y-2">
          <Label htmlFor="timeZoneId">Digest time zone</Label>
          <Input id="timeZoneId" value={form.data.timeZoneId} onChange={e => form.setData('timeZoneId', e.target.value)} placeholder="UTC" aria-describedby="time-zone-hint" />
          <p id="time-zone-hint" className="text-sm text-muted-foreground">Use an IANA time zone, for example Atlantic/Reykjavik.</p>
          {form.errors.timeZoneId && <p role="alert" className="text-sm text-destructive">{form.errors.timeZoneId}</p>}
        </div>

        <SaveButton processing={form.processing} recentlySuccessful={form.recentlySuccessful} label="Save preferences" />
      </Section>
    </form>
  )
}

export default function SettingsIndex({ profile, preferences }: SettingsPageProps) {
  return (
    <AppLayout title="Settings">
      <div className="mx-auto max-w-2xl">
        <PageHeader title="Settings" />
        <div className="mt-2 divide-y">
          <ProfileSection profile={profile} />
          <PreferencesSections preferences={preferences} />
        </div>
      </div>
    </AppLayout>
  )
}
