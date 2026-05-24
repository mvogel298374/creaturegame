import { useNavigate } from 'react-router-dom';

export function StarterSelection() {
  const nav = useNavigate();
  return (
    <div className="screen">
      <h2 className="screen-title">CHOOSE YOUR STARTER</h2>
      <p className="text-muted">Coming in Phase 5</p>
      <button className="btn-ghost" onClick={() => nav('/')}>← BACK</button>
    </div>
  );
}
