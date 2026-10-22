import type { VariantProps } from "class-variance-authority"
import { cva } from "class-variance-authority"

export { default as Button } from "./Button.vue"

export const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-full text-sm font-medium transition-[color,background-color,box-shadow,transform] active:scale-[0.98] disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-4 shrink-0 [&_svg]:shrink-0 outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-3 aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive",
  {
    variants: {
      variant: {
        default:
          "bg-primary text-primary-foreground hover:bg-primary/90",
        destructive:
          "bg-destructive text-white hover:bg-destructive/90 focus-visible:ring-destructive/20 dark:focus-visible:ring-destructive/40 dark:bg-destructive/60",
        outline:
          "border border-input bg-card hover:bg-muted dark:hover:bg-muted",
        secondary:
          "bg-secondary text-secondary-foreground hover:bg-muted-foreground/15",
        ghost:
          "hover:bg-muted",
        link: "text-primary underline-offset-4 hover:underline",
      },
      size: {
        "default": "h-11 px-5 md:h-10 has-[>svg]:px-4",
        "xs": "h-10 md:h-7 gap-1 px-2.5 text-xs has-[>svg]:px-2 [&_svg:not([class*='size-'])]:size-3",
        "sm": "h-10 gap-1.5 px-4 md:h-9 has-[>svg]:px-3",
        "lg": "h-12 px-7 text-base has-[>svg]:px-5",
        "icon": "size-11 md:size-10",
        "icon-xs": "size-10 md:size-7 [&_svg:not([class*='size-'])]:size-3.5",
        "icon-sm": "size-10 md:size-9",
        "icon-lg": "size-12",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
)
export type ButtonVariants = VariantProps<typeof buttonVariants>
