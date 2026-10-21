// Shared props sent on every page (see Program.cs AddInertia + Infrastructure/Shared.cs).

export type Theme = 'system' | 'light' | 'dark'

export interface AuthUser {
  id: string
  name: string
  email: string
  verified: boolean
  avatarUrl: string | null
  theme: Theme
}

export interface SharedProps {
  appName: string
  auth: { user: AuthUser | null }
  errors: Record<string, string>
  [key: string]: unknown
}

/** Flash keys used by the backend: Back().WithFlash("success" | "error" | "info", message). */
export interface Flash {
  success?: string
  error?: string
  info?: string
}
