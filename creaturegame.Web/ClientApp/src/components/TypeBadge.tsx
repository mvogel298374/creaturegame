import './TypeBadge.css';

// Every pair ≥ 4.5:1 (TypeBadge.test.ts); palette reasoning in docs/GENERATION_PROFILE.md (Kanto Sage budget).
export const TYPE_COLORS: Record<string, { bg: string; text: string }> = {
  Normal:   { bg: '#6e767e', text: '#fff' },
  Fire:     { bg: '#b65d06', text: '#fff' },
  Water:    { bg: '#3578bb', text: '#fff' },
  Electric: { bg: '#f3d23b', text: '#333' },
  Grass:    { bg: '#2c8625', text: '#fff' },
  Ice:      { bg: '#1f8276', text: '#fff' },
  Fighting: { bg: '#ce406a', text: '#fff' },
  Poison:   { bg: '#9b5ab7', text: '#fff' },
  Ground:   { bg: '#a14504', text: '#fff' },
  Flying:   { bg: '#5d74a6', text: '#fff' },
  Psychic:  { bg: '#b22f3c', text: '#fff' },
  Bug:      { bg: '#5c8001', text: '#fff' },
  Rock:     { bg: '#83744b', text: '#fff' },
  Ghost:    { bg: '#5269ac', text: '#fff' },
  Dragon:   { bg: '#0a6dc4', text: '#fff' },
  Dark:     { bg: '#5b5366', text: '#fff' },
  Steel:    { bg: '#487c90', text: '#fff' },
  Fairy:    { bg: '#ab53a7', text: '#fff' },
};

// The background colour for a type — shared with non-badge consumers (e.g. TypeChip, mapGlyphs.tsx) so type
// colouring stays defined in one place. Falls back to a neutral grey for an unknown type.
export function typeColor(type: string): string {
  return (TYPE_COLORS[type] ?? { bg: '#888' }).bg;
}

interface Props {
  type: string;
  size?: 'sm' | 'md';
}

export function TypeBadge({ type, size = 'sm' }: Props) {
  const colors = TYPE_COLORS[type] ?? { bg: '#888', text: '#fff' };
  return (
    <span
      className={`type-badge type-badge--${size}`}
      style={{ background: colors.bg, color: colors.text }}
    >
      {type.toUpperCase()}
    </span>
  );
}
