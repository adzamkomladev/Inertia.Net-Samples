export type Range = 7 | 30 | 90

export interface Summary {
  views: number
  likes: number
  comments: number
  posts: number
  prevViews: number
  prevLikes: number
  prevComments: number
}

export interface SeriesPoint {
  date: string
  views: number
  likes: number
  comments: number
}

export interface TopPost {
  id: string
  title: string
  views: number
  likes: number
  comments: number
  lifetimeViews: number
}
