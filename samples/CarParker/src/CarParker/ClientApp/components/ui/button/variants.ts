import { cn } from '@/lib/utils'

export type ButtonVariant = 'default' | 'navy' | 'destructive' | 'outline' | 'secondary' | 'ghost' | 'link'
export type ButtonSize = 'default' | 'xs' | 'sm' | 'lg' | 'icon' | 'icon-xs' | 'icon-sm' | 'icon-lg'

const base =
  "inline-flex shrink-0 items-center justify-center gap-2 rounded-lg text-sm font-semibold whitespace-nowrap transition-[color,background-color,box-shadow,transform] outline-none select-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4"

const variantClasses: Record<ButtonVariant, string> = {
  // Petrol is the one call-to-action colour; ochre is reserved for attention states.
  default: 'bg-primary text-primary-foreground hover:bg-primary/90 active:scale-[0.98]',
  navy: 'bg-foreground text-background hover:bg-foreground/90 active:scale-[0.98]',
  destructive: 'bg-destructive text-white hover:bg-destructive/90 dark:text-background active:scale-[0.98]',
  outline: 'bg-card text-foreground ring-1 ring-inset ring-input hover:bg-accent',
  secondary: 'bg-secondary text-secondary-foreground hover:bg-accent',
  ghost: 'hover:bg-accent hover:text-accent-foreground',
  link: 'text-primary underline-offset-4 hover:underline',
}

const sizeClasses: Record<ButtonSize, string> = {
  default: 'h-11 px-4 md:h-10',
  xs: "h-8 gap-1 rounded-md px-2.5 text-xs [&_svg:not([class*='size-'])]:size-3.5",
  sm: 'h-10 px-3.5 md:h-9',
  lg: 'h-12 rounded-xl px-6 text-base',
  icon: 'size-11 md:size-10',
  'icon-xs': "size-8 rounded-md [&_svg:not([class*='size-'])]:size-3.5",
  'icon-sm': 'size-10 md:size-9',
  'icon-lg': 'size-12 rounded-xl',
}

/** Button classes without the class-variance-authority machinery — same call shape, plain types. */
export function buttonVariants(options?: { variant?: ButtonVariant; size?: ButtonSize; class?: string }): string {
  return cn(
    base,
    variantClasses[options?.variant ?? 'default'],
    sizeClasses[options?.size ?? 'default'],
    options?.class,
  )
}
