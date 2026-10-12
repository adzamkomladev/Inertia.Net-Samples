import { router } from '@inertiajs/react'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import type { Range } from '@/types/analytics'

const ranges: Range[] = [7, 30, 90]

export default function RangeTabs({ range, only }: { range: Range; only: string[] }) {
  return (
    <Tabs
      value={String(range)}
      onValueChange={(v) => router.reload({ data: { range: Number(v) }, only: ['range', ...only] })}
    >
      <TabsList aria-label="Time range" className="h-auto rounded-lg bg-muted p-0.5">
        {ranges.map((r) => (
          <TabsTrigger
            key={r}
            value={String(r)}
            className="rounded-md px-3 py-1 text-[13px] data-[state=active]:bg-background data-[state=active]:shadow-xs dark:data-[state=active]:border-transparent dark:data-[state=active]:bg-background"
          >
            {r}d
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  )
}
