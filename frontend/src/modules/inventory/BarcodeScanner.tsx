import { useEffect, useRef, useState } from "react";
import { Button, Sheet } from "@/core/ui";

type Detector = { detect(source: HTMLVideoElement): Promise<{ rawValue: string }[]> };

const FORMATS = ["ean_13", "ean_8", "upc_a", "upc_e", "code_128", "code_39", "itf", "qr_code"];

/** Uses the native BarcodeDetector (Android Chrome) or a bundled zxing WebAssembly fallback (iOS, desktop). */
async function createDetector(): Promise<Detector> {
  const Native = (globalThis as { BarcodeDetector?: new (o: object) => Detector & { } }).BarcodeDetector;
  if (Native) {
    try { return new Native({ formats: FORMATS }); } catch { /* fall through to wasm */ }
  }
  const [{ BarcodeDetector, setZXingModuleOverrides }, { default: wasmUrl }] = await Promise.all([
    import("barcode-detector/ponyfill"),
    import("zxing-wasm/reader/zxing_reader.wasm?url"),
  ]);
  // Serve the wasm from our own origin instead of a CDN (works offline / on a LAN).
  setZXingModuleOverrides({ locateFile: (path: string, prefix: string) => (path.endsWith(".wasm") ? wasmUrl : prefix + path) });
  return new BarcodeDetector({ formats: FORMATS as never });
}

export function BarcodeScanner({ open, onClose, onDetected }: { open: boolean; onClose: () => void; onDetected: (code: string) => void }) {
  const video = useRef<HTMLVideoElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [manual, setManual] = useState("");

  useEffect(() => {
    if (!open) return;
    let stream: MediaStream | undefined;
    let raf = 0;
    let stopped = false;
    setError(null);

    (async () => {
      if (!navigator.mediaDevices?.getUserMedia) {
        setError(window.isSecureContext ? "This browser can't access the camera." : "The camera needs HTTPS. Open HomeStack over https:// (e.g. via Tailscale) to scan.");
        return;
      }
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" }, audio: false });
        if (stopped) return stream.getTracks().forEach((t) => t.stop());
        const v = video.current!;
        v.srcObject = stream;
        await v.play();
        const detector = await createDetector();
        let last = 0;
        const tick = async (t: number) => {
          if (stopped) return;
          if (t - last > 200 && v.readyState >= 2) {
            last = t;
            const codes = await detector.detect(v).catch(() => []);
            if (codes[0]?.rawValue && !stopped) {
              stopped = true;
              navigator.vibrate?.(60);
              onDetected(codes[0].rawValue);
              return;
            }
          }
          raf = requestAnimationFrame(tick);
        };
        raf = requestAnimationFrame(tick);
      } catch (e) {
        setError(e instanceof DOMException && e.name === "NotAllowedError" ? "Camera permission was denied." : `Couldn't start the camera: ${String(e)}`);
      }
    })();

    return () => {
      stopped = true;
      cancelAnimationFrame(raf);
      stream?.getTracks().forEach((t) => t.stop());
    };
  }, [open, onDetected]);

  return (
    <Sheet open={open} onClose={onClose} title="Scan barcode">
      <div className="flex flex-col gap-3">
        {error ? (
          <p className="rounded-xl bg-warn-soft px-3 py-2 text-sm text-warn">{error}</p>
        ) : (
          <div className="relative aspect-[4/3] overflow-hidden rounded-2xl bg-black">
            <video ref={video} playsInline muted className="size-full object-cover" />
            <div className="pointer-events-none absolute inset-x-8 top-1/2 h-24 -translate-y-1/2 rounded-xl border-2 border-white/80 shadow-[0_0_0_9999px_rgba(0,0,0,0.35)]" />
          </div>
        )}
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); if (manual.trim()) onDetected(manual.trim()); }}>
          <input inputMode="numeric" placeholder="…or type the number" value={manual} onChange={(e) => setManual(e.target.value)}
            className="min-h-11 w-full rounded-xl border border-border bg-surface px-3 outline-none focus:border-accent" />
          <Button>Look up</Button>
        </form>
      </div>
    </Sheet>
  );
}
