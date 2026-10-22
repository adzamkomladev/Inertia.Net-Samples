import { useEffect, useState } from 'react'
import { Volume2, VolumeX } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { soundEnabled, setSoundEnabled, unlockNotificationSound } from '@/lib/notification-sound'
export default function NotificationSound() {
  const [enabled, setEnabled] = useState(false)
  useEffect(() => {
    setEnabled(soundEnabled())
    const unlock = () => { void unlockNotificationSound().catch(() => {}) }
    document.addEventListener('pointerdown', unlock, { passive: true })
    return () => document.removeEventListener('pointerdown', unlock)
  }, [])
  async function toggle() {
    try { await setSoundEnabled(!enabled); setEnabled(soundEnabled()) } catch { setEnabled(false) }
  }
  return <Button variant="ghost" size="icon" aria-pressed={enabled} aria-label={enabled ? 'Mute notification sounds' : 'Enable notification sounds'} title={enabled ? 'Mute notification sounds' : 'Enable notification sounds'} onClick={toggle}>{enabled ? <Volume2 className="size-4" /> : <VolumeX className="size-4" />}</Button>
}
