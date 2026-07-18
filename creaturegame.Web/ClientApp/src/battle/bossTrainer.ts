// A themed "gate boss" trainer name for a biome's Boss node, so the encounter-map ladder reads
// "Boss / Trainer <Name>" instead of the behind-the-curtain "Region gate".
//
// Pure + deterministic on purpose: the ladder re-renders often, and a live Math.random() would reshuffle
// the name every frame. Seeding off the biome id + its revealed node plan gives a name that is themed by
// the biome, varies run-to-run (the node plan is per-run randomised — see ENCOUNTER_DESIGN.md), yet stays
// fixed for the duration of a single biome visit. Themed by the biome's primary type; unknown/untyped
// biomes fall back to a generic pool.

const NAMES_BY_TYPE: Record<string, string[]> = {
  Normal: ['Whitney', 'Norman', 'Dax', 'Rhea', 'Cole', 'Perrin'],
  Fire: ['Blaine', 'Cinder', 'Ember', 'Ignatia', 'Pyra', 'Ashlin'],
  Water: ['Misty', 'Marlon', 'Wade', 'Nerissa', 'Coral', 'Tidus'],
  Electric: ['Surge', 'Volt', 'Elektra', 'Sparx', 'Dynamo', 'Voltaire'],
  Grass: ['Erika', 'Fern', 'Bramble', 'Thorn', 'Willow', 'Verdi'],
  Ice: ['Pryce', 'Frost', 'Glacia', 'Icarus', 'Neve', 'Boreal'],
  Fighting: ['Bruno', 'Chuck', 'Maylene', 'Kaito', 'Brawn', 'Ryker'],
  Poison: ['Koga', 'Janine', 'Venn', 'Toxa', 'Morrow', 'Bane'],
  Ground: ['Giovanni', 'Bertha', 'Dune', 'Terra', 'Clay', 'Quarrick'],
  Flying: ['Falkner', 'Skyla', 'Gale', 'Aero', 'Zephyra', 'Cirrus'],
  Psychic: ['Sabrina', 'Will', 'Mesmer', 'Kismet', 'Oracle', 'Solace'],
  Bug: ['Bugsy', 'Aria', 'Weevil', 'Mantid', 'Chryssa', 'Hollis'],
  Rock: ['Brock', 'Roxanne', 'Cragg', 'Boulder', 'Sienna', 'Flint'],
  Ghost: ['Morty', 'Agatha', 'Fenwick', 'Wraith', 'Lumen', 'Shade'],
  Dragon: ['Lance', 'Clair', 'Draken', 'Wyverna', 'Ryu', 'Vesper'],
};

const GENERIC = ['Blue', 'Trace', 'Silver', 'Vale', 'Rex', 'Nova'];

// djb2 — a small, stable string hash. Deterministic across runs/machines (no locale/order surprises).
function hashString(s: string): number {
  let h = 5381;
  for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) >>> 0;
  return h >>> 0;
}

// The picked first name for a biome's boss. Callers usually render `Trainer ${bossTrainerName(...)}`.
export function bossTrainerName(
  biomeId: string,
  primaryType: string | undefined,
  nodePlan: readonly string[],
): string {
  const pool = (primaryType && NAMES_BY_TYPE[primaryType]) || GENERIC;
  const seed = `${biomeId}|${nodePlan.join(',')}`;
  return pool[hashString(seed) % pool.length];
}
