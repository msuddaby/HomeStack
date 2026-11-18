import { Link } from "react-router";
import { Settings, Sparkles } from "lucide-react";
import { useMe } from "@/core/auth";
import { PageHeader } from "@/core/layout";
import { useModules, type HomeSection } from "@/core/modules";
import { Card } from "@/core/ui";

function greeting() {
  const h = new Date().getHours();
  return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
}

/**
 * Home, ordered by urgency rather than by module: quick actions, then what needs dealing with now,
 * then today (meals, shopping), then how things stand.
 */
export function HomePage() {
  const me = useMe();
  const modules = useModules();
  const cards = (section: HomeSection) => modules.flatMap((m) => m.homeCards ?? [])
    .filter((c) => c.section === section).sort((a, b) => a.order - b.order)
    .map((c) => <c.Component key={c.id} />);

  return (
    <>
      <PageHeader title={`${greeting()}, ${me.data?.user.name ?? ""}`} subtitle={me.data?.household.name} action={
        // Settings has no phone tab; the desktop sidebar has it.
        <Link to="/settings" aria-label="Settings" className="press -mr-2 rounded-full p-2 text-muted hover:bg-surface-2 hover:text-text md:hidden">
          <Settings className="size-6" />
        </Link>
      } />
      <div className="flex flex-col gap-4 [--stack-gap:1rem]">
        <div className="grid grid-cols-2 gap-2 empty:hidden">{cards("actions")}</div>
        {/* Each card hides itself when there's nothing to deal with; if they all do, say so. */}
        <div className="peer flex flex-col gap-4 empty:hidden">{cards("attention")}</div>
        <Card className="animate-rise hidden peer-empty:block">
          <div className="flex items-center gap-3 text-sm"><Sparkles className="size-5 text-accent" />Nothing needs attention. Everything's fresh and paid.</div>
        </Card>
        {cards("today")}
        {cards("glance")}
      </div>
    </>
  );
}
