/** Calendar dates are "YYYY-MM-DD" strings in local time, matching the API's DateOnly. */
export function toISO(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
export function parseISO(s: string): Date {
  const [y, m, d] = s.split("-").map(Number);
  return new Date(y!, m! - 1, d!);
}
export const todayISO = () => toISO(new Date());
export function addDays(iso: string, days: number): string {
  const d = parseISO(iso);
  d.setDate(d.getDate() + days);
  return toISO(d);
}
export function daysUntil(iso: string, from = todayISO()): number {
  return Math.round((parseISO(iso).getTime() - parseISO(from).getTime()) / 86_400_000);
}
export function shortDate(iso: string): string {
  const d = parseISO(iso);
  const sameYear = d.getFullYear() === new Date().getFullYear();
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric", ...(sameYear ? {} : { year: "numeric" }) });
}
/** "3d", "5w", "4mo" style duration. */
export function duration(days: number): string {
  if (days < 14) return `${days}d`;
  if (days < 60) return `${Math.round(days / 7)}w`;
  return `${Math.round(days / 30)}mo`;
}
