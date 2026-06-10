import mitt from 'mitt';
import { E2E } from '../testEnv';

type BridgeEvents = {
  // React → Phaser
  enterBattle: { playerSpeciesId: number; enemySpeciesId: number };
  playMoveAnimation: { attackerSide: 'player' | 'enemy'; targetSide: 'player' | 'enemy' };
  playHitSound: { isCrit: boolean };
  playFaintAnimation: { side: 'player' | 'enemy' };
  playStatusSound: void;
  playLevelUpSound: void;
  // A new wild enemy for the next encounter in the chain — load + slide in the sprite.
  spawnEnemy: { enemySpeciesId: number };
  // Phaser → React
  entryComplete: void;
  animationComplete: void;
};

export const bridge = mitt<BridgeEvents>();

// E2E seam: expose the bridge and record every emitted event (with a timestamp)
// on window so Playwright can assert animation ordering — e.g. that playHitSound
// follows playMoveAnimation — without depending on wall-clock timing. No-op in prod.
if (E2E && typeof window !== 'undefined') {
  const w = window as unknown as { __cgBridge?: typeof bridge; __cgEvents?: { name: string; t: number }[] };
  w.__cgBridge = bridge;
  w.__cgEvents = [];
  bridge.on('*', (type) => w.__cgEvents!.push({ name: String(type), t: Math.round(performance.now()) }));
}
