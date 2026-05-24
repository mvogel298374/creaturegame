import { useNavigate } from 'react-router-dom';

export function BattleScreen() {
  const nav = useNavigate();
  return (
    <div className="screen">
      <h2 className="screen-title">BATTLE</h2>
      <p className="text-muted">Coming in Phase 6</p>
      <button className="btn-ghost" onClick={() => nav('/')}>← BACK</button>
    </div>
  );
}
