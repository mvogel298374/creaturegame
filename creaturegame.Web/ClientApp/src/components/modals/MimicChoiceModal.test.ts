import { describe, it, expect } from 'vitest';
import type { ReactElement } from 'react';
import { MimicChoiceModal } from './MimicChoiceModal';

type El = ReactElement<{ dismiss?: unknown; children?: unknown; onClick?: () => void }>;

describe('MimicChoiceModal — the Gen 1 Mimic menu: the foe\'s moves, no cancel', () => {
  const render = (onChoose: (i: number) => void) =>
    MimicChoiceModal({ prompt: { creatureName: 'DITTO', moves: ['tackle', 'growl', 'ember'] }, onChoose }) as El;

  it('is blocking, so Escape and a backdrop click do not close it', () => {
    expect(render(() => {}).props.dismiss).toBe('blocking');
  });

  it('offers one button per foe move, each answering with its slot index, and nothing else', () => {
    const picked: number[] = [];
    const [, grid, ...rest] = render(i => picked.push(i)).props.children as El[];
    const buttons = grid.props.children as El[];
    expect(rest).toHaveLength(0);
    expect(buttons).toHaveLength(3);
    buttons.forEach(b => b.props.onClick!());
    expect(picked).toEqual([0, 1, 2]);
  });
});
