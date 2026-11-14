import { useMe } from "@/core/auth";
import { PageHeader } from "@/core/layout";
import { modules } from "@/modules";

function greeting() {
  const h = new Date().getHours();
  return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
}

export function HomePage() {
  const me = useMe();
  return (
    <>
      <PageHeader title={`${greeting()}, ${me.data?.user.name ?? ""}`} subtitle={me.data?.household.name} />
      <div className="flex flex-col gap-4">
        {modules.map((m) => m.DashboardWidget && <m.DashboardWidget key={m.id} />)}
      </div>
    </>
  );
}
