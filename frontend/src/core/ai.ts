import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

export type AiProviderKind = "openAiCompatible" | "anthropic";
export interface AiSettings { configured: boolean; provider: AiProviderKind; baseUrl: string; hasApiKey: boolean; model: string; visionModel: string | null }
export interface AiTestResult { text: boolean; vision: boolean; model: string; visionModel: string | null; visionError: string | null }

export const AI_KEY = ["ai-settings"];
export const useAiSettings = () => useQuery({ queryKey: AI_KEY, queryFn: () => api<AiSettings>("/ai/settings"), staleTime: 60_000 });

export interface AiPreset { id: string; label: string; provider: AiProviderKind; baseUrl: string; model: string; needsKey: boolean; keyUrl?: string; hint: string }

/** Starting points; every field stays editable. Model ids change often, so the test button is the source of truth. */
export const AI_PRESETS: AiPreset[] = [
  { id: "deepseek", label: "DeepSeek", provider: "openAiCompatible", baseUrl: "https://api.deepseek.com", model: "deepseek-flash", needsKey: true,
    keyUrl: "https://platform.deepseek.com/api_keys", hint: "Cheap, and deepseek-flash reads photos." },
  { id: "claude", label: "Claude", provider: "anthropic", baseUrl: "https://api.anthropic.com", model: "claude-opus-5-5", needsKey: true,
    keyUrl: "https://console.anthropic.com/settings/keys", hint: "Best at faded or heavily abbreviated receipts." },
  { id: "openai", label: "OpenAI", provider: "openAiCompatible", baseUrl: "https://api.openai.com/v1", model: "", needsKey: true,
    keyUrl: "https://platform.openai.com/api-keys", hint: "Enter a current vision-capable model id." },
  { id: "ollama", label: "Ollama (local)", provider: "openAiCompatible", baseUrl: "http://host.docker.internal:11434/v1", model: "", needsKey: false,
    hint: "Runs on your own machine. Use a vision model you've pulled (see `ollama list`)." },
  { id: "custom", label: "Other", provider: "openAiCompatible", baseUrl: "", model: "", needsKey: false,
    hint: "Any OpenAI-compatible API: OpenRouter, LM Studio, Groq, vLLM…" },
];

/** Shrinks a phone photo to a sane upload (receipts stay legible at ~2000px on the long edge). */
export async function shrinkImage(file: File, maxEdge = 2000, quality = 0.85): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext("2d")!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return new Promise((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("Couldn't read that photo"))), "image/jpeg", quality));
}
