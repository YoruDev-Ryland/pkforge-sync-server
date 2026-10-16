const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAY = 86_400_000;
const midnight = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();

/** "just now", "2 min ago", "5 h ago", "yesterday", then a date ("Oct 3", with the year when it isn't this one). */
export function ago(when: Date | null, now = new Date()): string {
  if (!when) return 'never';
  const ms = now.getTime() - when.getTime();
  if (ms < 60_000) return 'just now';
  if (ms < 3_600_000) return `${Math.floor(ms / 60_000)} min ago`;
  const days = Math.round((midnight(now) - midnight(when)) / DAY);
  if (days <= 0) return `${Math.floor(ms / 3_600_000)} h ago`;
  if (days === 1) return 'yesterday';
  const date = `${MONTHS[when.getMonth()]} ${when.getDate()}`;
  return when.getFullYear() === now.getFullYear() ? date : `${date}, ${when.getFullYear()}`;
}

/** Time left on a code, as m:ss. */
export function countdown(ms: number): string {
  const s = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}
