import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the UI runs on :5173 and forwards API calls to the
// Issuarr backend on :8788 (start it with `dotnet run` in src/Issuarr.Host).
const backend = process.env.ISSUARR_URL ?? 'http://localhost:8788';

export default defineConfig({
  plugins: [react()],
  build: { target: 'es2022' },
  server: {
    port: 5173,
    proxy: {
      '/api': backend,
      '/initialize.json': backend,
    },
  },
});
