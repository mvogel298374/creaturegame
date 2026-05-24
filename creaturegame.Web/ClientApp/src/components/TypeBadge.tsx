import './TypeBadge.css';

const TYPE_COLORS: Record<string, { bg: string; text: string }> = {
  Normal:   { bg: '#9099a1', text: '#fff' },
  Fire:     { bg: '#ff9c54', text: '#fff' },
  Water:    { bg: '#4d90d5', text: '#fff' },
  Electric: { bg: '#f3d23b', text: '#333' },
  Grass:    { bg: '#63bb5b', text: '#fff' },
  Ice:      { bg: '#74cec0', text: '#fff' },
  Fighting: { bg: '#ce406a', text: '#fff' },
  Poison:   { bg: '#ab6ac8', text: '#fff' },
  Ground:   { bg: '#d97845', text: '#fff' },
  Flying:   { bg: '#8fa8dd', text: '#fff' },
  Psychic:  { bg: '#f97176', text: '#fff' },
  Bug:      { bg: '#90c12c', text: '#fff' },
  Rock:     { bg: '#c7b78b', text: '#fff' },
  Ghost:    { bg: '#5269ac', text: '#fff' },
  Dragon:   { bg: '#0a6dc4', text: '#fff' },
  Dark:     { bg: '#5b5366', text: '#fff' },
  Steel:    { bg: '#5a8ea2', text: '#fff' },
  Fairy:    { bg: '#ec8fe6', text: '#fff' },
};

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
