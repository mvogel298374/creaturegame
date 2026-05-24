import { useNavigate } from 'react-router-dom';

export function StarterSelection() {
  const nav = useNavigate();
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', minHeight: '100vh', gap: '1rem' }}>
      <h2 style={{ letterSpacing: '4px', fontSize: '1.5rem' }}>CHOOSE YOUR STARTER</h2>
      <p style={{ color: '#555', fontSize: '0.8rem', letterSpacing: '2px' }}>Coming in Phase 5</p>
      <button onClick={() => nav('/')} style={{ marginTop: '1rem', padding: '0.6rem 1.5rem', background: 'transparent', color: '#87CEEB', border: '1px solid #87CEEB', letterSpacing: '2px', fontSize: '0.85rem' }}>
        ← BACK
      </button>
    </div>
  );
}
