import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";
import path from "node:path";

export default defineConfig({
  resolve: { alias: { "@": path.resolve(import.meta.dirname, "src") } },
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: "autoUpdate",
      includeAssets: ["icon.svg"],
      manifest: {
        name: "HomeStack",
        short_name: "HomeStack",
        description: "Your household, organised.",
        theme_color: "#15803d",
        background_color: "#fafaf9",
        display: "standalone",
        start_url: "/",
        icons: [
          { src: "icon.svg", sizes: "any", type: "image/svg+xml", purpose: "any" },
          { src: "icon.svg", sizes: "any", type: "image/svg+xml", purpose: "maskable" },
        ],
      },
      workbox: {
        // Never cache API responses in the service worker; data must be live.
        navigateFallbackDenylist: [/^\/api\//],
        globPatterns: ["**/*.{js,css,html,svg,png,woff2,wasm}"],
        maximumFileSizeToCacheInBytes: 5 * 1024 * 1024,
      },
    }),
  ],
  server: {
    host: true,
    // Allow access through `tailscale serve` (https://<machine>.<tailnet>.ts.net).
    allowedHosts: [".ts.net"],
    proxy: { "/api": "http://localhost:5080" },
  },
  build: { outDir: "dist" },
});
