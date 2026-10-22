let context: AudioContext | undefined
export function soundEnabled(): boolean {
  if (typeof window === 'undefined') return false
  try { return localStorage.getItem('notification-sound') === 'on' } catch { return false }
}
export async function setSoundEnabled(enabled: boolean): Promise<void> {
  try { localStorage.setItem('notification-sound', enabled ? 'on' : 'off') } catch { /* Browser storage may be disabled. */ }
  if (enabled) {
    context ??= new AudioContext()
    await context.resume()
    playNotificationSound()
  }
}
export function playNotificationSound(): void {
  if (!soundEnabled() || context?.state !== 'running') return
  const oscillator = context.createOscillator()
  const gain = context.createGain()
  oscillator.type = 'sine'
  oscillator.frequency.setValueAtTime(740, context.currentTime)
  oscillator.frequency.setValueAtTime(990, context.currentTime + 0.1)
  gain.gain.setValueAtTime(0.06, context.currentTime)
  gain.gain.exponentialRampToValueAtTime(0.001, context.currentTime + 0.3)
  oscillator.connect(gain)
  gain.connect(context.destination)
  oscillator.start()
  oscillator.stop(context.currentTime + 0.3)
}
export async function unlockNotificationSound(): Promise<void> {
  if (soundEnabled()) { context ??= new AudioContext(); await context.resume() }
}
