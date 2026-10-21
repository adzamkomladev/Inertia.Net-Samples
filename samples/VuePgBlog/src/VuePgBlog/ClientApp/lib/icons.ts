import {
  Camera, Cable, Cpu, Gamepad2, Headphones, Package, Smartphone, Speaker, Tv, Watch, Zap,
  type Component,
} from '@lucide/vue'

/**
 * Lucide icon names stored on categories (CategorySeeds / admin category form)
 * mapped to components. Unknown names fall back to Package.
 */
export const categoryIcons: Record<string, Component> = {
  Headphones, Cpu, Smartphone, Camera, Tv, Gamepad2, Watch, Speaker, Zap, Cable, Package,
}

export function categoryIcon(name?: string | null): Component {
  return categoryIcons[(name ?? '').replace(/[^a-zA-Z0-9]/g, '')] ?? Package
}
