export type ChartConfig = Record<string, { label: string; color: string }>

/** Renders the crosshair tooltip for a hit-tested datum, styled like the shadcn chart tooltip. */
export function tooltipHtml(config: ChartConfig, label: (x: number | Date) => string) {
  return (data: Record<string, any> | { data: Record<string, any> }, x: number | Date) => {
    const source = data && typeof data === 'object' && 'data' in data ? data.data : data
    const rows = Object.entries(source ?? {})
      .filter(([key]) => key in config)
      .map(([key, value]) => `<div class="flex w-full items-center gap-2"><div class="h-2.5 w-2.5 shrink-0 rounded-xs" style="background-color:${config[key]!.color}"></div><span class="text-muted-foreground flex-1">${config[key]!.label}</span><span class="text-foreground font-mono font-medium tabular-nums">${Number(value).toLocaleString()}</span></div>`)
      .join('')
    return `<div class="border-border/50 bg-background grid min-w-[8rem] items-start gap-1.5 rounded-lg border px-2.5 py-1.5 text-xs shadow-xl"><div class="font-medium">${label(x)}</div><div class="grid gap-1.5">${rows}</div></div>`
  }
}

/** CSS variables shared by the chart wrappers (axis and tooltip theming). */
export const chartTheme = [
  '--vis-tooltip-padding:0px',
  '--vis-tooltip-background-color:transparent',
  '--vis-tooltip-border-color:transparent',
  '--vis-tooltip-text-color:none',
  '--vis-tooltip-shadow-color:none',
  '--vis-tooltip-backdrop-filter:none',
  '--vis-crosshair-circle-stroke-color:#0000',
  '--vis-crosshair-line-stroke-width:1px',
  '--vis-font-family:var(--font-sans)',
].join(';')

export const chartSurfaceClass =
  '[&_.tick_text]:!fill-muted-foreground [&_.tick_line]:!stroke-border/50 flex h-72 w-full flex-col justify-center text-xs'
