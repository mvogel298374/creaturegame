/**
 * GB-style battle SFX synthesiser.
 *
 * Gen 1 used the Game Boy's pulse/square wave channels for all battle sounds.
 * Square oscillator + fast frequency envelope reproduces that punchy character.
 */

let ctx: AudioContext | null = null;
let masterGain: GainNode | null = null;
// Applied to masterGain.gain once it exists; read by getMasterVolume() before then. Kept separate from the
// gain node itself so setMasterVolume() never has to create the AudioContext (would trip the browser's
// autoplay-policy warning before any sound has actually played, e.g. from a Settings screen applying a
// persisted volume at app boot).
let pendingVolume = 1;

function ac(): AudioContext {
  if (!ctx) ctx = new AudioContext();
  return ctx;
}

// Single volume control point: every sound in this module routes through this one gain node instead of
// straight to the destination, so a live slider adjusts sounds already mid-decay, not just ones started
// after the change.
function master(): GainNode {
  const a = ac();
  if (!masterGain) {
    masterGain = a.createGain();
    masterGain.gain.value = pendingVolume;
    masterGain.connect(a.destination);
  }
  return masterGain;
}

/** Sets the master volume (0–1) for all AudioEngine output; clamped to that range. */
export function setMasterVolume(v: number): void {
  pendingVolume = Math.min(1, Math.max(0, v));
  if (masterGain) masterGain.gain.value = pendingVolume;
}

/** Reads the current master volume (0–1); default 1 (unchanged from historical behaviour). */
export function getMasterVolume(): number {
  return pendingVolume;
}

// ── Primitive builder ─────────────────────────────────────────────────────────

interface SquareBurst {
  startFreq: number;
  endFreq: number;
  duration: number;   // seconds
  volume?: number;    // 0–1, default 0.35
  type?: OscillatorType;
  delayStart?: number; // seconds before this burst plays
}

function burst({ startFreq, endFreq, duration, volume = 0.35, type = 'square', delayStart = 0 }: SquareBurst) {
  const a = ac();
  const t = a.currentTime + delayStart;

  const osc  = a.createOscillator();
  const gain = a.createGain();
  osc.connect(gain);
  gain.connect(master());

  osc.type = type;
  osc.frequency.setValueAtTime(startFreq, t);
  osc.frequency.exponentialRampToValueAtTime(Math.max(endFreq, 10), t + duration);

  gain.gain.setValueAtTime(volume, t);
  gain.gain.exponentialRampToValueAtTime(0.001, t + duration);

  osc.start(t);
  osc.stop(t + duration + 0.01);
}

// ── Battle SFX ────────────────────────────────────────────────────────────────

/** Low thwack hit — sine drop (200→50 Hz) + short noise transient */
export function playHit(): void {
  const a = ac();
  const t = a.currentTime;

  // Sine body: low tonal thump
  const osc     = a.createOscillator();
  const oscGain = a.createGain();
  osc.type = 'sine';
  osc.connect(oscGain);
  oscGain.connect(master());
  osc.frequency.setValueAtTime(200, t);
  osc.frequency.exponentialRampToValueAtTime(50, t + 0.08);
  oscGain.gain.setValueAtTime(0.55, t);
  oscGain.gain.exponentialRampToValueAtTime(0.001, t + 0.13);
  osc.start(t);
  osc.stop(t + 0.14);

  // Noise transient: the "crack" at impact
  const size   = Math.floor(a.sampleRate * 0.03);
  const buf    = a.createBuffer(1, size, a.sampleRate);
  const data   = buf.getChannelData(0);
  for (let i = 0; i < size; i++)
    data[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / size, 3);
  const noise     = a.createBufferSource();
  const noiseGain = a.createGain();
  noise.buffer = buf;
  noise.connect(noiseGain);
  noiseGain.connect(master());
  noiseGain.gain.setValueAtTime(0.35, t);
  noiseGain.gain.exponentialRampToValueAtTime(0.001, t + 0.03);
  noise.start(t);
}

/** Critical hit — same body, higher pitch + extra crack layer */
export function playHitCrit(): void {
  const a = ac();
  const t = a.currentTime;

  const osc     = a.createOscillator();
  const oscGain = a.createGain();
  osc.type = 'sine';
  osc.connect(oscGain);
  oscGain.connect(master());
  osc.frequency.setValueAtTime(320, t);
  osc.frequency.exponentialRampToValueAtTime(80, t + 0.08);
  oscGain.gain.setValueAtTime(0.6, t);
  oscGain.gain.exponentialRampToValueAtTime(0.001, t + 0.13);
  osc.start(t);
  osc.stop(t + 0.14);

  // Extra high-pitched crack for the crit
  burst({ startFreq: 1200, endFreq: 400, duration: 0.05, volume: 0.25 });

  const size   = Math.floor(a.sampleRate * 0.04);
  const buf    = a.createBuffer(1, size, a.sampleRate);
  const data   = buf.getChannelData(0);
  for (let i = 0; i < size; i++)
    data[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / size, 2);
  const noise     = a.createBufferSource();
  const noiseGain = a.createGain();
  noise.buffer = buf;
  noise.connect(noiseGain);
  noiseGain.connect(master());
  noiseGain.gain.setValueAtTime(0.45, t);
  noiseGain.gain.exponentialRampToValueAtTime(0.001, t + 0.04);
  noise.start(t);
}

/** Damage tick sound for HP/XP bars */
export function playTick(): void {
  burst({ startFreq: 660, endFreq: 600, duration: 0.03, volume: 0.12 });
}

/** Status condition applied (sleep/poison/etc.) */
export function playStatusApplied(): void {
  // Two descending bleeps
  burst({ startFreq: 500, endFreq: 300, duration: 0.07, volume: 0.28 });
  burst({ startFreq: 380, endFreq: 220, duration: 0.08, volume: 0.22, delayStart: 0.09 });
}

/** Level-up fanfare — quick ascending arpeggio */
export function playLevelUp(): void {
  const notes = [523, 659, 784, 1047]; // C5 E5 G5 C6
  notes.forEach((freq, i) => {
    burst({ startFreq: freq, endFreq: freq * 0.97, duration: 0.10, volume: 0.3, delayStart: i * 0.09 });
  });
}

// ── Pokémon cries (fallback when OGG files are not yet downloaded) ─────────────

export function playCry(speciesId: number): void {
  const baseFreq = 220 + (speciesId % 60) * 8;
  burst({ startFreq: baseFreq,        endFreq: baseFreq * 1.4, duration: 0.08, volume: 0.25, type: 'square' });
  burst({ startFreq: baseFreq * 1.4,  endFreq: baseFreq * 0.85, duration: 0.14, volume: 0.22, type: 'square', delayStart: 0.07 });
}

export function playFaintCry(): void {
  burst({ startFreq: 400, endFreq: 80, duration: 0.6, volume: 0.2, type: 'square' });
}
