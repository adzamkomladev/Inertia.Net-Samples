// Shared contract between the C# endpoints (camelCase JSON) and the Vue pages.

export interface AuthUser {
  id: string
  name: string
  email: string
  verified: boolean
  avatarUrl: string | null
  theme: Theme
}

export type Theme = 'system' | 'light' | 'dark'

export interface Toast {
  type: 'success' | 'error' | 'info'
  message: string
}

export interface Author {
  id: string
  name: string
  avatarUrl: string | null
}

export interface UserSummary extends Author {
  isSelf?: boolean
  following: boolean
  followerCount: number
  followingCount: number
}

export interface PostSummary {
  reposter?: Author | null
  reason?: string | null
  topics?: string[]
  bookmarked?: boolean
  reposted?: boolean
  archivedAt?: string | null
  canArchive?: boolean
  id: string
  slug: string
  title: string
  excerpt: string
  /** FTS snippet with <mark> around matches; only present when searching. Already HTML-escaped server-side. */
  snippet: string | null
  coverUrl: string | null
  author: Author
  likeCount: number
  commentCount: number
  viewCount: number
  liked: boolean
  repostCount: number
  createdAt: string
}

export interface PostDetail extends PostSummary {
  body: string
  coverAssetId: string | null
  updatedAt: string
  liked: boolean
  canEdit: boolean
}

export interface CommentItem {
  cursor?: string
  liked?: boolean
  likeCount?: number
  id: string
  body: string
  createdAt: string
  author: Author
  canDelete: boolean
}

/** Shape of an Inertia `Scroll(...)` prop (wrapper "data"). */
export interface ScrollPage<T> {
  data: T[]
}

export interface UploadedAsset {
  id: string
  url: string
  contentType: string
  size: number
}

export interface PresignResponse {
  assetId: string
  url: string
  headers: Record<string, string>
}

export interface Preferences {
  theme: Theme
  emailOnComment: boolean
  emailOnLike: boolean
  weeklyDigest: boolean
  dailyRecommendationDigest: boolean
  timeZoneId: string
  postsPerPage: number
}

export interface Kpis {
  views: number
  uniqueViewers: number
  likes: number
  comments: number
  posts: number
}

export interface SeriesPoint {
  date: string // yyyy-MM-dd
  views: number
  uniqueViewers: number
  likes: number
  comments: number
}

export interface TopPost {
  id: string
  slug: string
  title: string
  views: number
  likes: number
  comments: number
}

export interface Referrer {
  host: string
  views: number
}

declare module '@inertiajs/core' {
  export interface InertiaConfig {
    sharedPageProps: {
      appName: string
      auth: { user: AuthUser | null }
    }
    flashDataType: {
      toast?: Toast
    }
    errorValueType: string
  }
}
