import { useEffect, useRef } from 'react';
import './BattleCanvas.css';

interface Props {
  playerSpeciesId: number;
  enemySpeciesId: number;
}

export function BattleCanvas({ playerSpeciesId, enemySpeciesId }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!containerRef.current) return;

    let game: import('phaser').Game | null = null;
    let cancelled = false;

    Promise.all([
      import('phaser'),
      import('./BattleScene'),
    ]).then(([{ default: Phaser }, { BattleScene }]) => {
      if (cancelled || !containerRef.current) return;

      game = new Phaser.Game({
        type: Phaser.AUTO,
        parent: containerRef.current,
        width: 480,
        height: 320,
        transparent: true,
        backgroundColor: 'transparent',
        scale: {
          mode: Phaser.Scale.RESIZE,
          autoCenter: Phaser.Scale.NONE,
        },
      });

      // Add and auto-start the scene with species IDs so init(data) receives them
      game.scene.add('BattleScene', BattleScene, true, { playerSpeciesId, enemySpeciesId });
    });

    return () => {
      cancelled = true;
      game?.destroy(true);
    };
  // IDs are stable for the battle's lifetime
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return <div ref={containerRef} className="battle-canvas-container" />;
}
