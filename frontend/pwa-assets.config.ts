import { defineConfig, minimal2023Preset } from "@vite-pwa/assets-generator/config";

// Regenerate the PNG app icons from public/icon.svg with `npm run icons`, then commit them.
// iPhone and Android "maskable" icons are cropped by the phone, so fill them edge to edge with the brand green.
const green = "#15803d";

export default defineConfig({
  preset: {
    ...minimal2023Preset,
    apple: { ...minimal2023Preset.apple, padding: 0.1, resizeOptions: { background: green } },
    maskable: { ...minimal2023Preset.maskable, padding: 0.15, resizeOptions: { background: green } },
  },
  images: ["public/icon.svg"],
});
