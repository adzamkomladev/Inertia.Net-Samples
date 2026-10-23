// DTOs mirroring Features/Posts/PostsFeature.cs (camelCase). Timestamps are UTC ISO strings.

export interface Author {
  id: string
  name: string
  avatarUrl: string | null
}

export interface SocialPost {
  id: string
  topics?: string[]
  bookmarked?: boolean
  reposted?: boolean
  archivedAt?: string | null
  canArchive?: boolean
  reposter?: Author | null
  reason?: string | null
}
export interface UserSummary extends Author {
  bio: string | null
  joinedAt: string
  following: boolean
  followerCount: number
  followingCount: number
  isSelf?: boolean
}
export interface TopicCount {
  topic: string
  count: number
}

export interface PostCard extends SocialPost {
  id: string
  title: string
  excerpt: string
  /** HTML-encoded except for <mark> tags; only set for search results. */
  snippet: string | null
  coverUrl: string | null
  createdAt: string
  author: Author
  likeCount: number
  commentCount: number
  repostCount: number
  viewCount: number
  liked: boolean
}

export interface PostDetail extends SocialPost {
  isPublished?: boolean
  id: string
  title: string
  body: string
  coverUrl: string | null
  createdAt: string
  updatedAt: string
  author: Author
  likeCount: number
  commentCount: number
  repostCount: number
  viewCount: number
  liked: boolean
}

export interface PostComment {
  cursor?: string
  liked?: boolean
  likeCount?: number
  id: string
  body: string
  createdAt: string
  author: Author
  canDelete: boolean
}

export interface PostEdit {
  isPublished?: boolean
  topics?: string[]
  id: string
  title: string
  body: string
  coverUrl: string | null
}

export interface PostFormData {
  isPublished?: boolean
  topics?: string[]
  title: string
  body: string
  coverUrl: string | null
}
