import { router } from '@inertiajs/react'
import { Loader2 } from 'lucide-react'
import { useState } from 'react'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import type { Range } from '@/types/analytics'

const ranges: Range[] = [7, 30, 90]

export default function RangeTabs({ range, only }: { range: Range; only: string[] }) {
  const [pending, setPending] = useState<Range | null>(null)
  return (
    <Tabs
      value={String(pending ?? range)}
      onValueChange={(v) => router.reload({
        data: { range: Number(v) }, only: ['range', ...only],
        onStart: () => setPending(Number(v) as Range),
        onFinish: () => setPending(null),
      })}
    >
      <TabsList aria-label="Time range" aria-busy={pending !== null} className="h-auto rounded-full bg-muted p-0.5">
        {ranges.map((r) => (
          <TabsTrigger
            key={r}
            value={String(r)}
            className="min-h-9 gap-1 rounded-full px-3.5 py-1 text-[13px] data-[state=active]:bg-background data-[state=active]:shadow-xs dark:data-[state=active]:border-transparent dark:data-[state=active]:bg-background"
          >
            {pending === r && <Loader2 className="size-3 animate-spin" aria-hidden />}
            {r}d
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  )
}
