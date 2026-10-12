import type { Theme } from '@/types/shared'

export interface ProfileSettings {
  displayName: string
  email: string
  avatarUrl: string | null
}

export interface PreferenceSettings {
  theme: Theme
  emailOnComment: boolean
  emailOnLike: boolean
  weeklyDigest: boolean
  dailyRecommendationDigest?: boolean
  timeZoneId?: string
}

export interface SettingsPageProps {
  profile: ProfileSettings
  preferences: PreferenceSettings
}
