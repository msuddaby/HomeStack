import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";
import path from "node:path";

export default defineConfig({
  resolve: { alias: { "@": path.resolve(import.meta.dirname, "src") } },
  // Shown in Settings, so you can tell whether a phone is running the latest build or a cached one.
  define: { __BUILD_TIME__: JSON.stringify(new Date().toISOString()) },
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: "autoUpdate",
      includeAssets: ["icon.svg", "favicon.ico", "apple-touch-icon-180x180.png"],
      manifest: {
        name: "HomeStack",
        short_name: "HomeStack",
        description: "Your household, organised.",
        theme_color: "#15803d",
        background_color: "#fafaf9",
        display: "standalone",
        start_url: "/",
        icons: [
          { src: "pwa-64x64.png", sizes: "64x64", type: "image/png" },
          { src: "pwa-192x192.png", sizes: "192x192", type: "image/png" },
          { src: "pwa-512x512.png", sizes: "512x512", type: "image/png", purpose: "any" },
          { src: "maskable-icon-512x512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
          { src: "icon.svg", sizes: "any", type: "image/svg+xml", purpose: "any" },
        ],
      },
      workbox: {
        // Never cache API responses in the service worker; data must be live.
        navigateFallbackDenylist: [/^\/api\//],
        globPatterns: ["**/*.{js,css,html,svg,png,woff2,wasm}"],
        maximumFileSizeToCacheInBytes: 5 * 1024 * 1024,
        // Showing notifications and opening them (public/push-sw.js).
        importScripts: ["/push-sw.js"],
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
