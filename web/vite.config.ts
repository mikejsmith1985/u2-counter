import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    // Loopback only. Nothing here should be reachable from the network during
    // development, and binding every interface is how that happens by accident.
    host: "127.0.0.1",
    port: 5173,
    proxy: {
      // The API is proxied so the browser sees one origin, which keeps cookies
      // simple and removes cross-origin handling from the development path.
      "/api": {
        target: "http://127.0.0.1:5080",
        changeOrigin: false,
      },
    },
  },
  build: {
    outDir: "dist",
    sourcemap: true,
  },
});
