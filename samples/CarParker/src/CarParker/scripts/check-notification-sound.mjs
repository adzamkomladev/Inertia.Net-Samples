import assert from 'node:assert/strict'
import { soundEnabled, setSoundEnabled, playNotificationSound, unlockNotificationSound } from '../ClientApp/lib/notification-sound.ts'
assert.equal(soundEnabled(), false, 'SSR is silent')
let setting = null
let chimes = 0
let resumes = 0
globalThis.window = {}
globalThis.localStorage = { getItem: () => setting, setItem: (_, value) => { setting = value } }
globalThis.AudioContext = class {
  state = 'suspended'
  currentTime = 0
  destination = {}
  async resume() { resumes++; this.state = 'running' }
  createOscillator() { return { frequency: { setValueAtTime() {} }, connect() {}, start() { chimes++ }, stop() {} } }
  createGain() { return { gain: { setValueAtTime() {}, exponentialRampToValueAtTime() {} }, connect() {} } }
}
playNotificationSound()
assert.equal(chimes, 0, 'disabled sound stays silent')
await setSoundEnabled(true)
assert.equal(soundEnabled(), true)
assert.equal(chimes, 1, 'enabling plays a test chime')
playNotificationSound()
assert.equal(chimes, 2, 'notification plays after browser unlock')
await setSoundEnabled(false)
playNotificationSound()
assert.equal(chimes, 2, 'muting stops playback')
assert.equal(resumes, 1)
await unlockNotificationSound()
assert.equal(resumes, 1, 'muted interaction does not unlock audio')
localStorage.setItem('notification-sound', 'on')
await unlockNotificationSound()
assert.equal(resumes, 2, 'saved opt-in unlocks after a gesture')
console.log('Notification sound checks passed')
