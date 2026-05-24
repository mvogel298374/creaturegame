import { Routes, Route } from 'react-router-dom';
import { TitleScreen } from './pages/TitleScreen';
import { StarterSelection } from './pages/StarterSelection';
import { BattleScreen } from './pages/BattleScreen';

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<TitleScreen />} />
      <Route path="/select" element={<StarterSelection />} />
      <Route path="/battle" element={<BattleScreen />} />
    </Routes>
  );
}
