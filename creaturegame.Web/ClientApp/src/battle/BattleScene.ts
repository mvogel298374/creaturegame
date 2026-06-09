import Phaser from 'phaser';
import { bridge } from './PhaserBridge';
import * as Audio from './AudioEngine';
import { E2E } from '../testEnv';

export class BattleScene extends Phaser.Scene {
  private playerSprite!: Phaser.GameObjects.Image;
  private enemySprite!: Phaser.GameObjects.Image;
  private playerIdleTween!: Phaser.Tweens.Tween;
  private enemyIdleTween!: Phaser.Tweens.Tween;
  private playerSpeciesId = 1;
  private enemySpeciesId = 1;
  private criesAvailable = false;

  // Kept so we can remove exactly these listeners on teardown — Phaser never
  // calls a method named destroy(), so listeners must be cleaned up via the
  // scene's SHUTDOWN/DESTROY events or they leak across remounts (HMR, StrictMode).
  private onMoveAnim   = (e: { attackerSide: 'player' | 'enemy'; targetSide: 'player' | 'enemy' }) =>
    this.playMoveAnimation(e.attackerSide, e.targetSide);
  private onFaintAnim  = (e: { side: 'player' | 'enemy' }) => this.playFaintAnimation(e.side);
  private onHitSound    = (e: { isCrit: boolean }) => (e.isCrit ? Audio.playHitCrit() : Audio.playHit());
  private onStatusSound = () => Audio.playStatusApplied();
  private onSpawnEnemy  = (e: { enemySpeciesId: number }) => this.spawnEnemy(e.enemySpeciesId);

  constructor() {
    super({ key: 'BattleScene' });
  }

  init(data: { playerSpeciesId: number; enemySpeciesId: number }) {
    this.playerSpeciesId = data.playerSpeciesId;
    this.enemySpeciesId = data.enemySpeciesId;
  }

  preload() {
    this.load.image('player', `/sprites/back/${this.playerSpeciesId}.png`);
    this.load.image('enemy', `/sprites/front/${this.enemySpeciesId}.png`);

    // Attempt to load OGG cries; if the files don't exist yet (importer not run)
    // the load will silently fail and we fall back to Web Audio synth
    this.load.audio(`cry-player`, `/audio/cries/${this.playerSpeciesId}.ogg`);
    this.load.audio(`cry-enemy`,  `/audio/cries/${this.enemySpeciesId}.ogg`);

    this.load.once(Phaser.Loader.Events.COMPLETE, () => {
      this.criesAvailable =
        this.cache.audio.exists('cry-player') &&
        this.cache.audio.exists('cry-enemy');
    });
  }

  create() {
    const W = this.scale.width;
    const H = this.scale.height;

    // E2E: run all tweens and timers (entry slide, the 1.8s cry pause, lunges,
    // faint) much faster so battles play through quickly under test.
    if (E2E) { this.tweens.timeScale = 8; this.time.timeScale = 8; }

    const enemyRestX = W * 0.68;
    const enemyRestY = H * 0.30;
    const playerRestX = W * 0.28;
    const playerRestY = H * 0.65;

    // Scale sprites to a fixed proportion of canvas height — caps large Pokémon
    // Gen 1 sprites are 96×96 px source
    const enemyScale  = Math.min(2.5, (H * 0.22) / 96);
    const playerScale = Math.min(3.0, (H * 0.28) / 96);

    this.enemySprite = this.add.image(W + 120, enemyRestY, 'enemy').setScale(enemyScale);
    this.playerSprite = this.add.image(-120, playerRestY, 'player').setScale(playerScale);

    bridge.on('playMoveAnimation', this.onMoveAnim);
    bridge.on('playFaintAnimation', this.onFaintAnim);
    bridge.on('playHitSound', this.onHitSound);
    bridge.on('playStatusSound', this.onStatusSound);
    bridge.on('spawnEnemy', this.onSpawnEnemy);

    // Remove our bridge listeners when this scene is torn down so they can't
    // fire on a destroyed scene (which throws and freezes the battle queue).
    this.events.once(Phaser.Scenes.Events.SHUTDOWN, this.teardown, this);
    this.events.once(Phaser.Scenes.Events.DESTROY, this.teardown, this);

    this.playEntryAnimation(enemyRestX, playerRestX);
  }

  private playCry(who: 'player' | 'enemy', detune = 0) {
    if (this.criesAvailable) {
      const key = who === 'player' ? 'cry-player' : 'cry-enemy';
      this.sound.play(key, { volume: 0.7, detune });
    } else {
      const id = who === 'player' ? this.playerSpeciesId : this.enemySpeciesId;
      Audio.playCry(id);
    }
  }

  private playEntryAnimation(enemyRestX: number, playerRestX: number) {
    this.tweens.add({
      targets: this.enemySprite,
      x: enemyRestX,
      duration: 400,
      ease: 'Cubic.easeOut',
      onComplete: () => {
        this.playCry('enemy');
        // Pause after enemy cry before player enters
        this.time.delayedCall(1800, () => {
          this.tweens.add({
            targets: this.playerSprite,
            x: playerRestX,
            duration: 400,
            ease: 'Cubic.easeOut',
            onComplete: () => {
              this.playCry('player');
              this.startIdleTweens();
              bridge.emit('entryComplete', undefined);
            },
          });
        });
      },
    });
  }

  private startIdleTweens() {
    this.enemyIdleTween = this.tweens.add({
      targets: this.enemySprite,
      y: `-=5`,
      yoyo: true,
      repeat: -1,
      duration: 700,
      ease: 'Sine.easeInOut',
    });

    this.playerIdleTween = this.tweens.add({
      targets: this.playerSprite,
      y: `-=5`,
      yoyo: true,
      repeat: -1,
      duration: 700,
      ease: 'Sine.easeInOut',
      delay: 200,
    });
  }

  private playMoveAnimation(attackerSide: 'player' | 'enemy', targetSide: 'player' | 'enemy') {
    const attacker = attackerSide === 'player' ? this.playerSprite : this.enemySprite;
    const target   = targetSide   === 'player' ? this.playerSprite : this.enemySprite;
    const attackerIdle = attackerSide === 'player' ? this.playerIdleTween : this.enemyIdleTween;

    const lunge  = attackerSide === 'player' ? 50 : -50;
    const originX = attacker.x;

    attackerIdle?.pause();

    this.tweens.add({
      targets: attacker,
      x: originX + lunge,
      duration: 150,
      ease: 'Cubic.easeIn',
      onComplete: () => {
        target.setTint(0xffffff);
        this.time.delayedCall(80, () => target.clearTint());

        this.tweens.add({
          targets: attacker,
          x: originX,
          duration: 200,
          ease: 'Cubic.easeOut',
          onComplete: () => {
            attackerIdle?.resume();
            bridge.emit('animationComplete', undefined);
          },
        });
      },
    });
  }

  private playFaintAnimation(side: 'player' | 'enemy') {
    const sprite = side === 'player' ? this.playerSprite : this.enemySprite;
    const idle   = side === 'player' ? this.playerIdleTween : this.enemyIdleTween;

    idle?.pause();
    // Play cry at lower pitch (–600 cents = one octave down) for the faint
    this.playCry(side, -600);

    this.tweens.add({
      targets: sprite,
      y: sprite.y + 80,
      alpha: 0,
      duration: 500,
      ease: 'Cubic.easeIn',
      onComplete: () => {
        bridge.emit('animationComplete', undefined);
      },
    });
  }

  // A new wild enemy for the next encounter: load its sprite (if not cached), reset the slot the previous
  // enemy fainted out of (alpha/position), then slide the newcomer in and resume the idle bob. The player
  // sprite and the canvas persist across the whole run — only the enemy is swapped.
  private spawnEnemy(enemySpeciesId: number) {
    this.enemySpeciesId = enemySpeciesId;
    const key = `enemy-${enemySpeciesId}`;

    const reveal = () => {
      const W = this.scale.width;
      const H = this.scale.height;
      const enemyRestX = W * 0.68;
      const enemyRestY = H * 0.3;

      this.enemyIdleTween?.stop();
      this.enemySprite.setTexture(key);
      this.enemySprite.setAlpha(1).setPosition(W + 120, enemyRestY);

      this.tweens.add({
        targets: this.enemySprite,
        x: enemyRestX,
        duration: 400,
        ease: 'Cubic.easeOut',
        onComplete: () => {
          this.playCry('enemy');
          this.enemyIdleTween = this.tweens.add({
            targets: this.enemySprite,
            y: `-=5`,
            yoyo: true,
            repeat: -1,
            duration: 700,
            ease: 'Sine.easeInOut',
          });
        },
      });
    };

    if (this.textures.exists(key)) {
      reveal();
    } else {
      this.load.image(key, `/sprites/front/${enemySpeciesId}.png`);
      this.load.once(Phaser.Loader.Events.COMPLETE, reveal);
      this.load.start();
    }
  }

  private teardown() {
    bridge.off('playMoveAnimation', this.onMoveAnim);
    bridge.off('playFaintAnimation', this.onFaintAnim);
    bridge.off('playHitSound', this.onHitSound);
    bridge.off('playStatusSound', this.onStatusSound);
    bridge.off('spawnEnemy', this.onSpawnEnemy);
  }
}
