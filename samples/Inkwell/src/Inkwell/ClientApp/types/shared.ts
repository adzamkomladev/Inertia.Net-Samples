// Shared props sent on every page (see Program.cs AddInertia + Infrastructure/Shared.cs).

export type Theme = 'system' | 'light' | 'dark'

export interface AuthUser {
  id: string
  name: string
  email: string
  verified: boolean
  isAdmin?: boolean
  avatarUrl: string | null
  theme: Theme
}

export interface SharedProps {
  notifications?: NotificationSummary | null
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

export type NoticeKind = 'like' | 'comment' | 'notice'
export interface InboxActor { id: string; name: string; avatarUrl: string | null }
export interface InboxNotice {
  id: string
  kind: NoticeKind
  /** Plain-text fallback when there is no actor or essay to name. */
  title: string
  href: string
  at: string
  readAt: string | null
  actor: InboxActor | null
  postTitle: string | null
}
export interface NotificationSummary { unreadCount: number; items: InboxNotice[] }
