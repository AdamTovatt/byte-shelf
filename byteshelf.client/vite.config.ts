import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import svgr from 'vite-plugin-svgr';
import { env } from 'process';

const target = env.ASPNETCORE_URLS
  ? env.ASPNETCORE_URLS.split(';')[0].replace('localhost', '127.0.0.1')
  : 'http://127.0.0.1:5190';

export default defineConfig({
  plugins: [svgr(), plugin()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  // Listens on localhost only; `npm run dev -- --host <address>` serves it elsewhere.
  server: {
    proxy: {
      '^/api/*': {
        target,
        secure: false
      }
    },
    // SpaProxyServerUrl in ByteShelf.Server.csproj names this port.
    port: 44911,
    strictPort: true,
    open: false,
  }
})
