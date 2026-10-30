import { defineConfig } from 'vite';
const proxy={'/ws': {target: 'ws://localhost:8787', ws: true}};
export default defineConfig({server: {proxy}, preview:{proxy}});
