import mitt from 'mitt';
import { E2E } from '../testEnv';

type BridgeEvents = {
  // React → Phaser
  enterBattle: { playerSpeciesId: number; enemySpeciesId: number };
  playMoveAnimation: { attackerSide: 'player' | 'enemy'; targetSide: 'player' | 'enemy' };
  playHitSound: { isCrit: boolean };
  playFaintAnimation: { side: 'player' | 'enemy' };
  // Hit reaction: a quick horizontal jolt on the sprite that just took damage (fire-and-forget; not awaited).
  playDamageShake: { side: 'player' | 'enemy' };
  playStatusSound: void;
  playLevelUpSound: void;
  // A new wild enemy for the next encounter in the chain — load + slide in the sprite.
  spawnEnemy: { enemySpeciesId: number };
  // Transform (Ditto/Mew): morph the named side's sprite to the copied species (player → back sprite,
  // enemy → front sprite).
  transformSprite: { side: 'player' | 'enemy'; speciesId: number };
  // Revert the player sprite to its true species after a battle ends — Transform is undone at battle end,
  // and the player sprite (unlike the enemy's) isn't otherwise reset across the endless chain.
  resetPlayerSprite: void;
  // Evolution (permanent): morph the player sprite into the evolved species with the classic Gen 1
  // white-silhouette flicker. Awaited by the timeline (emits animationComplete on finish).
  playEvolutionAnimation: { toSpeciesId: number };
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
