import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { TypeBadge } from '../components/TypeBadge';
import { NicknameModal } from '../components/modals/NicknameModal';
import type { Species } from '../types/Species';
import { friendlyFetchError } from '../utils/fetchError';
import { buildStartGameRequest } from '../utils/startGameRequest';
import { saveActiveGame } from '../utils/activeGame';
import { DEFAULT_GENERATION } from '../generations/presentation';
import './StarterSelection.css';

export function StarterSelection() {
  const nav = useNavigate();
  const [species, setSpecies]   = useState<Species[]>([]);
  const [filtered, setFiltered] = useState<Species[]>([]);
  const [selected, setSelected] = useState<Species | null>(null);
  const [search, setSearch]     = useState('');
  const [loading, setLoading]   = useState(true);
  const [error, setError]       = useState<string | null>(null);
  const [levelChoice, setLevelChoice] = useState(50);
  // The nickname step opens after CONFIRM, before the request fires — see NicknameModal.
  const [naming, setNaming] = useState(false);
  // Roguelite difficulty: a per-run choice (like Level/Seed), threaded once at game start into the backend's
  // RunRules preset (docs/TODO.md — Settings Menu). Not a Settings-menu / localStorage concern.
  const [difficultyChoice, setDifficultyChoice] = useState<'Easy' | 'Normal' | 'Hard'>('Normal');
  // The run's generation (Generation Profile Stage 4a). A constant until the generation picker lands
  // (Stage 4d+, GENERATION_PROFILE.md §7.5 #8) — but the channel is live: sent in the start body (the
  // server parses it with the same fallback contract as Difficulty) and carried in route state for the
  // immediate theme, with the server echo as the authority thereafter.
  const generationChoice = DEFAULT_GENERATION;
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    fetch('/api/species')
      .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); })
      .then((data: Species[]) => { setSpecies(data); setFiltered(data); setLoading(false); })
      .catch(e => { setError(friendlyFetchError(e)); setLoading(false); });
  }, []);

  useEffect(() => {
    const q = search.toLowerCase().trim();
    setFiltered(q ? species.filter(s => s.name.toLowerCase().includes(q)) : species);
  }, [search, species]);

  const confirm = async (nickname: string | null) => {
    if (!selected) return;
    try {
      const body = buildStartGameRequest({
        speciesId: selected.id,
        level: levelChoice,
        difficulty: difficultyChoice,
        generation: generationChoice,
        nickname,
        seedParam: new URLSearchParams(window.location.search).get('seed'),
      });

      const res = await fetch('/api/game/start', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const { gameId } = await res.json() as { gameId: string };
      // Persisted for the lightweight resume feature (Session Resume) — BattleScreen falls back to this on a
      // refresh/reopen with no nav state, and TitleScreen offers it as a Continue option.
      saveActiveGame({ gameId, species: selected, level: levelChoice, generation: generationChoice });
      nav('/battle', { state: { species: selected, gameId, level: levelChoice, generation: generationChoice } });
    } catch (e) {
      setNaming(false);
      setError(friendlyFetchError(e));
    }
  };

  return (
    <div className="select-screen">
      <header className="select-header">
        <button className="btn-ghost" onClick={() => nav('/')}>← BACK</button>
        <h1 className="select-heading">CHOOSE YOUR STARTER</h1>
        <span className="select-count">
          {loading ? '…' : `${filtered.length} / ${species.length}`}
        </span>
      </header>

      <div className="select-search-bar">
        <input
          ref={searchRef}
          className="select-search"
          type="text"
          placeholder="Search by name…"
          value={search}
          onChange={e => setSearch(e.target.value)}
          autoFocus
        />
        {search && (
          <button className="select-search-clear" onClick={() => setSearch('')}>✕</button>
        )}
      </div>

      <main className="select-main">
        {loading && <p className="text-muted" style={{ padding: 'var(--sp-xl)' }}>Loading…</p>}
        {error   && <p className="text-muted" style={{ padding: 'var(--sp-xl)', color: 'var(--clr-accent)' }}>{error}</p>}
        {!loading && !error && filtered.length === 0 && (
          <p className="text-muted" style={{ padding: 'var(--sp-xl)' }}>No results for "{search}"</p>
        )}
        <div className="species-grid">
          {filtered.map(s => (
            <SpeciesCard
              key={s.id}
              species={s}
              isSelected={s.id === selected?.id}
              onClick={() => setSelected(prev => prev?.id === s.id ? null : s)}
            />
          ))}
        </div>
      </main>

      <div className="level-picker">
        <span className="level-picker-label">LEVEL</span>
        <input
          className="level-picker-slider"
          type="range"
          min={5}
          max={100}
          step={1}
          value={levelChoice}
          onChange={e => setLevelChoice(Number(e.target.value))}
        />
        <span className="level-picker-value">{levelChoice}</span>

        <span className="level-picker-label difficulty-picker-label">DIFFICULTY</span>
        <div className="difficulty-picker" role="radiogroup" aria-label="Difficulty">
          {(['Easy', 'Normal', 'Hard'] as const).map(tier => (
            <button
              key={tier}
              type="button"
              role="radio"
              aria-checked={difficultyChoice === tier}
              className={`difficulty-btn ${difficultyChoice === tier ? 'difficulty-btn--selected' : ''}`}
              onClick={() => setDifficultyChoice(tier)}
            >
              {tier.toUpperCase()}
            </button>
          ))}
        </div>
      </div>

      <footer className={`select-footer ${selected ? 'select-footer--visible' : ''}`}>
        {selected && (
          <>
            <div className="footer-info">
              <span className="footer-name">{selected.name.toUpperCase()}</span>
              <div className="footer-types">
                <TypeBadge type={selected.type1} size="md" />
                {selected.type2 && <TypeBadge type={selected.type2} size="md" />}
              </div>
              <span className="footer-bst">BST {selected.baseStatTotal}</span>
            </div>
            <button className="btn" onClick={() => setNaming(true)}>CONFIRM →</button>
          </>
        )}
      </footer>

      {naming && selected && (
        <NicknameModal speciesName={selected.name.toUpperCase()} onDone={confirm} />
      )}
    </div>
  );
}

function SpeciesCard({ species, isSelected, onClick }: {
  species: Species;
  isSelected: boolean;
  onClick: () => void;
}) {
  const [imgFailed, setImgFailed] = useState(false);

  return (
    <div
      className={`species-card ${isSelected ? 'species-card--selected' : ''}`}
      onClick={onClick}
      role="button"
      tabIndex={0}
      onKeyDown={e => e.key === 'Enter' && onClick()}
    >
      <span className="card-number">#{String(species.id).padStart(3, '0')}</span>

      <div className="card-sprite-wrap">
        {imgFailed ? (
          <div className="card-sprite-placeholder" />
        ) : (
          <img
            className="card-sprite"
            src={`/sprites/front/${species.id}.png`}
            alt={species.name}
            onError={() => setImgFailed(true)}
          />
        )}
      </div>

      <span className="card-name">{species.name.toUpperCase()}</span>

      <div className="card-types">
        <TypeBadge type={species.type1} />
        {species.type2 && <TypeBadge type={species.type2} />}
      </div>

      <span className="card-bst">BST {species.baseStatTotal}</span>
    </div>
  );
}
