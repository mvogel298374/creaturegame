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
  private playerTrueSpeciesId = 1; // the player's real species, restored after a Transform reverts
  private initialPlayerSpeciesId = 1; // the species loaded under the 'player' texture key (pre-evolution)

  // Kept so we can remove exactly these listeners on teardown — Phaser never
  // calls a method named destroy(), so listeners must be cleaned up via the
  // scene's SHUTDOWN/DESTROY events or they leak across remounts (HMR, StrictMode).
  private onMoveAnim   = (e: { attackerSide: 'player' | 'enemy'; targetSide: 'player' | 'enemy' }) =>
    this.playMoveAnimation(e.attackerSide, e.targetSide);
  private onFaintAnim  = (e: { side: 'player' | 'enemy' }) => this.playFaintAnimation(e.side);
  private onDamageShake = (e: { side: 'player' | 'enemy' }) => this.shakeSprite(e.side);
  private onHitSound    = (e: { isCrit: boolean }) => (e.isCrit ? Audio.playHitCrit() : Audio.playHit());
  private onStatusSound = () => Audio.playStatusApplied();
  private onLevelUpSound = () => Audio.playLevelUp();
  private onSpawnEnemy  = (e: { enemySpeciesId: number }) => this.spawnEnemy(e.enemySpeciesId);
  private onTransformSprite = (e: { side: 'player' | 'enemy'; speciesId: number }) =>
    this.transformSprite(e.side, e.speciesId);
  private onResetPlayerSprite = () => this.resetPlayerSprite();
  private onSwapPlayer = (e: { speciesId: number }) => this.swapPlayerCreature(e.speciesId);
  private onEvolve = (e: { toSpeciesId: number }) => this.playEvolutionAnimation(e.toSpeciesId);

  constructor() {
    super({ key: 'BattleScene' });
  }

  init(data: { playerSpeciesId: number; enemySpeciesId: number }) {
    this.playerSpeciesId = data.playerSpeciesId;
    this.playerTrueSpeciesId = data.playerSpeciesId;
    this.initialPlayerSpeciesId = data.playerSpeciesId;
    this.enemySpeciesId = data.enemySpeciesId;
  }

  preload() {
    this.load.image('player', `/sprites/back/${this.playerSpeciesId}.png`);
    this.load.image('enemy', `/sprites/front/${this.enemySpeciesId}.png`);

    // A missing OGG fails silently; playCry falls back to the synth (SPRITE_PRESENTATION.md §1.6).
    this.queueCry(this.playerSpeciesId);
    this.queueCry(this.enemySpeciesId);
  }

  // Per-species cry texture/audio key.
  private cryKey(speciesId: number) {
    return `cry-${speciesId}`;
  }

  // Queue a species' OGG cry for loading if it isn't already cached. The caller owns load.start()/COMPLETE;
  // for preload the scene's boot loader runs it. Idempotent — re-queuing an existing key is skipped.
  private queueCry(speciesId: number) {
    const key = this.cryKey(speciesId);
    if (!this.cache.audio.exists(key))
      this.load.audio(key, `/audio/cries/${speciesId}.ogg`);
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

    // Scaled off 96×96 source sprites, capped so large species don't overrun the canvas (SPRITE_PRESENTATION.md §1.3).
    const enemyScale  = Math.min(2.5, (H * 0.22) / 96);
    const playerScale = Math.min(3.0, (H * 0.28) / 96);

    this.enemySprite = this.add.image(W + 120, enemyRestY, 'enemy').setScale(enemyScale);
    this.playerSprite = this.add.image(-120, playerRestY, 'player').setScale(playerScale);

    bridge.on('playMoveAnimation', this.onMoveAnim);
    bridge.on('playFaintAnimation', this.onFaintAnim);
    bridge.on('playDamageShake', this.onDamageShake);
    bridge.on('playHitSound', this.onHitSound);
    bridge.on('playStatusSound', this.onStatusSound);
    bridge.on('playLevelUpSound', this.onLevelUpSound);
    bridge.on('spawnEnemy', this.onSpawnEnemy);
    bridge.on('transformSprite', this.onTransformSprite);
    bridge.on('resetPlayerSprite', this.onResetPlayerSprite);
    bridge.on('swapPlayerCreature', this.onSwapPlayer);
    bridge.on('playEvolutionAnimation', this.onEvolve);

    // A bridge listener firing on a destroyed scene throws and freezes the battle queue — teardown() removes
    // them on both events so a leftover listener can't hit that.
    this.events.once(Phaser.Scenes.Events.SHUTDOWN, this.teardown, this);
    this.events.once(Phaser.Scenes.Events.DESTROY, this.teardown, this);

    this.playEntryAnimation(enemyRestX, playerRestX);
  }

  private playCry(who: 'player' | 'enemy', detune = 0) {
    const id = who === 'player' ? this.playerSpeciesId : this.enemySpeciesId;
    const key = this.cryKey(id);
    if (this.cache.audio.exists(key)) {
      // Scaled explicitly by the master volume — a separate pipeline from AudioEngine (SPRITE_PRESENTATION.md §1.6).
      this.sound.play(key, { volume: 0.7 * Audio.getMasterVolume(), detune });
    } else {
      Audio.playCry(id); // synth fallback
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

  // Fire-and-forget hit jolt, directional (away from the attacker); x-only so it doesn't fight the y idle bob.
  private shakeSprite(side: 'player' | 'enemy') {
    const sprite = side === 'player' ? this.playerSprite : this.enemySprite;
    const originX = sprite.x;
    const amp = side === 'player' ? -8 : 8;
    this.tweens.add({
      targets: sprite,
      x: originX + amp,
      duration: 45,
      ease: 'Sine.easeInOut',
      yoyo: true,
      repeat: 2,
      onComplete: () => { sprite.x = originX; },
    });
  }

  // A new wild enemy for the next encounter — load on demand + slide in (SPRITE_PRESENTATION.md §1.3).
  private spawnEnemy(enemySpeciesId: number) {
    this.enemySpeciesId = enemySpeciesId;
    const spriteKey = `enemy-${enemySpeciesId}`;

    const reveal = () => {
      const W = this.scale.width;
      const H = this.scale.height;
      const enemyRestX = W * 0.68;
      const enemyRestY = H * 0.3;

      this.enemyIdleTween?.stop();
      this.enemySprite.setTexture(spriteKey);
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

    const cryKey = this.cryKey(enemySpeciesId);
    if (this.textures.exists(spriteKey) && this.cache.audio.exists(cryKey)) {
      reveal();
    } else {
      if (!this.textures.exists(spriteKey))
        this.load.image(spriteKey, `/sprites/front/${enemySpeciesId}.png`);
      this.queueCry(enemySpeciesId);
      this.load.once(Phaser.Loader.Events.COMPLETE, reveal);
      this.load.start();
    }
  }

  // Transform (Ditto/Mew): morph in place, no slide-in — temporary, so only playerSpeciesId updates, not
  // playerTrueSpeciesId (SPRITE_PRESENTATION.md §1.3 "True-species tracking").
  private transformSprite(side: 'player' | 'enemy', speciesId: number) {
    const dir = side === 'player' ? 'back' : 'front';
    const key = `${dir}-${speciesId}`;
    const sprite = side === 'player' ? this.playerSprite : this.enemySprite;
    if (side === 'player') this.playerSpeciesId = speciesId;
    else this.enemySpeciesId = speciesId;

    const morph = () => {
      const baseSx = sprite.scaleX;
      const baseSy = sprite.scaleY;
      sprite.setTexture(key);
      // A brief scale pulse as the morph cue.
      this.tweens.add({
        targets: sprite,
        scaleX: baseSx * 1.12,
        scaleY: baseSy * 1.12,
        duration: 130,
        yoyo: true,
        ease: 'Sine.easeInOut',
      });
    };

    const cryKey = this.cryKey(speciesId);
    if (this.textures.exists(key) && this.cache.audio.exists(cryKey)) {
      morph();
    } else {
      if (!this.textures.exists(key)) this.load.image(key, `/sprites/${dir}/${speciesId}.png`);
      this.queueCry(speciesId); // so a faint after Transform cries as the copied species
      this.load.once(Phaser.Loader.Events.COMPLETE, morph);
      this.load.start();
    }
  }

  // Evolution (permanent): classic Gen 1 white-silhouette flicker, then settles on the evolved sprite —
  // updates both tracked species ids (SPRITE_PRESENTATION.md §1.3). Awaited by the timeline.
  private playEvolutionAnimation(toSpeciesId: number) {
    const newKey = `back-${toSpeciesId}`;
    const sprite = this.playerSprite;

    const run = () => {
      this.playerIdleTween?.pause();
      const oldKey = sprite.texture.key;
      const flickerEvery = 110;
      const flips = 8;

      const flip = (n: number) => {
        if (n < flips) {
          sprite.setTexture(n % 2 === 0 ? newKey : oldKey);
          sprite.setTintFill(0xffffff); // solid white silhouette of the morphing shape
          this.time.delayedCall(flickerEvery, () => flip(n + 1));
          return;
        }
        // Settle: evolved sprite, full colour, species (and true species) updated.
        sprite.setTexture(newKey);
        sprite.clearTint();
        this.playerSpeciesId = toSpeciesId;
        this.playerTrueSpeciesId = toSpeciesId;
        this.playCry('player');
        this.playerIdleTween?.resume();
        bridge.emit('animationComplete', undefined);
      };

      flip(0);
    };

    const cryKey = this.cryKey(toSpeciesId);
    if (this.textures.exists(newKey) && this.cache.audio.exists(cryKey)) {
      run();
    } else {
      if (!this.textures.exists(newKey)) this.load.image(newKey, `/sprites/back/${toSpeciesId}.png`);
      this.queueCry(toSpeciesId); // the settle plays the evolved cry, not the pre-evolution one
      this.load.once(Phaser.Loader.Events.COMPLETE, run);
      this.load.start();
    }
  }

  // Forced faint-switch / voluntary switch: slides the incoming species' back sprite in, mirroring spawnEnemy
  // for the player side. A real creature change like evolution — updates both tracked species ids
  // (SPRITE_PRESENTATION.md §1.3). Fire-and-forget: the timeline paces the beat with its own waits.
  private swapPlayerCreature(speciesId: number) {
    this.playerSpeciesId = speciesId;
    this.playerTrueSpeciesId = speciesId;
    const key = `back-${speciesId}`;

    const reveal = () => {
      const W = this.scale.width;
      const H = this.scale.height;
      const playerRestX = W * 0.28;
      const playerRestY = H * 0.65;

      this.playerIdleTween?.stop();
      this.playerSprite.setTexture(key);
      this.playerSprite.setAlpha(1).setPosition(-120, playerRestY);

      this.tweens.add({
        targets: this.playerSprite,
        x: playerRestX,
        duration: 400,
        ease: 'Cubic.easeOut',
        onComplete: () => {
          this.playCry('player');
          this.playerIdleTween = this.tweens.add({
            targets: this.playerSprite,
            y: `-=5`,
            yoyo: true,
            repeat: -1,
            duration: 700,
            ease: 'Sine.easeInOut',
          });
        },
      });
    };

    const cryKey = this.cryKey(speciesId);
    if (this.textures.exists(key) && this.cache.audio.exists(cryKey)) {
      reveal();
    } else {
      if (!this.textures.exists(key)) this.load.image(key, `/sprites/back/${speciesId}.png`);
      this.queueCry(speciesId);
      this.load.once(Phaser.Loader.Events.COMPLETE, reveal);
      this.load.start();
    }
  }

  // Reverts to the true species at battle end — key choice explained in SPRITE_PRESENTATION.md §1.3.
  private resetPlayerSprite() {
    this.playerSpeciesId = this.playerTrueSpeciesId;
    const key =
      this.playerTrueSpeciesId === this.initialPlayerSpeciesId
        ? 'player'
        : `back-${this.playerTrueSpeciesId}`;
    this.playerSprite.setTexture(key);
  }

  private teardown() {
    bridge.off('playMoveAnimation', this.onMoveAnim);
    bridge.off('playFaintAnimation', this.onFaintAnim);
    bridge.off('playDamageShake', this.onDamageShake);
    bridge.off('playHitSound', this.onHitSound);
    bridge.off('playStatusSound', this.onStatusSound);
    bridge.off('playLevelUpSound', this.onLevelUpSound);
    bridge.off('spawnEnemy', this.onSpawnEnemy);
    bridge.off('transformSprite', this.onTransformSprite);
    bridge.off('resetPlayerSprite', this.onResetPlayerSprite);
    bridge.off('swapPlayerCreature', this.onSwapPlayer);
    bridge.off('playEvolutionAnimation', this.onEvolve);
  }
}
