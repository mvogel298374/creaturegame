// Vite's `?raw` suffix imports a file's source text as a string. Declared here (rather than pulling in all of
// `vite/client`) because the one consumer is a source-scanning guard test (`battle/routing.test.ts`).
declare module '*?raw' {
  const source: string;
  export default source;
}
