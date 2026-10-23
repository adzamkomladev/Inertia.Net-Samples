import { Check, ChevronsUpDown, Globe2, LocateFixed } from 'lucide-react'
import { useState } from 'react'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

// The runtime's IANA list; "UTC" is not always in it but the server accepts it.
const zones = (() => {
  try { return ['UTC', ...Intl.supportedValuesOf('timeZone').filter(z => z !== 'UTC')] } catch { return ['UTC'] }
})()

function offset(zone: string) {
  try {
    return new Intl.DateTimeFormat('en', { timeZone: zone, timeZoneName: 'shortOffset' }).formatToParts(new Date())
      .find(p => p.type === 'timeZoneName')?.value.replace('GMT', 'UTC') ?? ''
  } catch { return '' }
}

const label = (zone: string) => zone.replace(/_/g, ' ')

/** Searchable IANA time zone picker with a one-tap "use this device's zone" shortcut. */
export default function TimeZonePicker({ id, value, onChange, invalid, describedBy }: {
  id: string
  value: string
  onChange: (zone: string) => void
  invalid?: boolean
  describedBy?: string
}) {
  const [open, setOpen] = useState(false)
  const local = Intl.DateTimeFormat().resolvedOptions().timeZone
  return (
    <div className="flex flex-wrap items-center gap-2">
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button
            id={id} type="button" variant="outline" role="combobox" aria-expanded={open} aria-invalid={invalid} aria-describedby={describedBy}
            className="h-10 min-w-0 flex-1 justify-between rounded-lg font-normal sm:max-w-sm"
          >
            <span className="flex min-w-0 items-center gap-2">
              <Globe2 className="text-muted-foreground" aria-hidden />
              <span className="truncate">{label(value)}</span>
              <span className="shrink-0 text-xs text-muted-foreground">{offset(value)}</span>
            </span>
            <ChevronsUpDown className="opacity-50" aria-hidden />
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-[min(22rem,calc(100vw-2rem))] p-0" align="start">
          <Command>
            <CommandInput placeholder="Search city or region…" />
            <CommandList className="max-h-72">
              <CommandEmpty>No time zone found.</CommandEmpty>
              <CommandGroup>
                {zones.map(zone => (
                  <CommandItem key={zone} value={zone} keywords={[label(zone)]} onSelect={() => { onChange(zone); setOpen(false) }}>
                    <Check className={cn('size-4', zone === value ? 'opacity-100' : 'opacity-0')} aria-hidden />
                    <span className="flex-1 truncate">{label(zone)}</span>
                    <span className="text-xs text-muted-foreground">{offset(zone)}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      {local && local !== value && (
        <Button type="button" variant="ghost" size="sm" className="rounded-full text-muted-foreground" onClick={() => onChange(local)}>
          <LocateFixed aria-hidden /> Use {label(local)}
        </Button>
      )}
    </div>
  )
}
