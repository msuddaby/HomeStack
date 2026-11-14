import { useState } from "react";
import { useNavigate } from "react-router";
import { Download } from "lucide-react";
import { Button, ErrorText, Field, Input, Sheet } from "@/core/ui";
import { importRecipe } from "./api";

/** Pulls a recipe from a web page, then opens it in the editor to review before saving. */
export function ImportSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [url, setUrl] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const navigate = useNavigate();

  const go = async () => {
    setBusy(true); setError(null);
    try {
      const draft = await importRecipe(url);
      onClose();
      setUrl("");
      navigate("/meals/recipes/new", { state: { draft } });
    } catch (e) { setError(e); } finally { setBusy(false); }
  };

  return (
    <Sheet open={open} onClose={onClose} title="Import a recipe">
      <form className="flex flex-col gap-3" onSubmit={(e) => { e.preventDefault(); go(); }}>
        <Field label="Recipe page link" hint="Works with most recipe sites. You'll review it before saving.">
          <Input type="url" inputMode="url" autoFocus placeholder="https://…" value={url} onChange={(e) => setUrl(e.target.value)} required />
        </Field>
        <ErrorText error={error} />
        <Button variant="primary" busy={busy}><Download className="size-4" />Import</Button>
      </form>
    </Sheet>
  );
}
