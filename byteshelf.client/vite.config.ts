import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import svgr from 'vite-plugin-svgr';
import { env } from 'process';

const target = env.ASPNETCORE_URLS
  ? env.ASPNETCORE_URLS.split(';')[0].replace('localhost', '127.0.0.1')
  : 'http://127.0.0.1:5190';

const toServer = { target, secure: false };

export default defineConfig({
  plugins: [svgr(), plugin()],
  // Listens on localhost only; `npm run dev -- --host <address>` serves it elsewhere.
  server: {
    proxy: {
      '^/api/*': toServer,
      // What the server answers outside the API: MCP for agents, the text agents read first, and the
      // API explorer, so the dev port serves them as the server's own port does.
      '/mcp': toServer,
      '/llms.txt': toServer,
      '/swagger': toServer,
    },
    // SpaProxyServerUrl in ByteShelf.Server.csproj names this port. It is the house dev port, the one
    // a dev subdomain's reverse proxy forwards to, so another app's dev server on it must stop first.
    port: 44903,
    strictPort: true,
    open: false,
    // Requests arrive through that proxy under its own host name. `true` turns off Vite's DNS-rebinding
    // check, because the proxied name differs per developer and the dev-server launcher sets no
    // variable naming it, so there is no name to list here. Listing it needs the launcher to pass it.
    allowedHosts: true,
  }
})
