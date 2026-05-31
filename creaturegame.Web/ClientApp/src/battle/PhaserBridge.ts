import mitt from 'mitt';

type BridgeEvents = {
  // React → Phaser
  enterBattle: { playerSpeciesId: number; enemySpeciesId: number };
  playMoveAnimation: { attackerSide: 'player' | 'enemy'; targetSide: 'player' | 'enemy' };
  playHitSound: { isCrit: boolean };
  playFaintAnimation: { side: 'player' | 'enemy' };
  playStatusSound: void;
  // Phaser → React
  entryComplete: void;
  animationComplete: void;
};

export const bridge = mitt<BridgeEvents>();
