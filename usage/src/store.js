import { aggregate } from './aggregate.js';

// Raw reports are kept this many days, then only the daily totals.
export const KEEP_DAYS = 90;

export function utcDay(date) {
  return date.toISOString().slice(0, 10);
}

export function addDays(day, days) {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return utcDay(date);
}

// Keeps the latest report for its ID. An ID that arrives on a later day than it first did is
// moved to that day: its node's clock and this one disagree about midnight, and it counts once.
export async function putReport(db, report, day) {
  await db
    .prepare('INSERT INTO reports (id, day, body) VALUES (?1, ?2, ?3) ON CONFLICT (id) DO UPDATE SET day = excluded.day, body = excluded.body')
    .bind(report.id, day, JSON.stringify(report))
    .run();
}

// Adds up every whole day before today that has reports and no totals yet, then removes reports
// older than KEEP_DAYS. Run once a day; running it again changes nothing.
export async function rollUp(db, today) {
  const { results: days } = await db
    .prepare('SELECT DISTINCT day FROM reports WHERE day < ?1 AND day NOT IN (SELECT day FROM daily) ORDER BY day')
    .bind(today)
    .all();

  for (const { day } of days) {
    const { results } = await db.prepare('SELECT body FROM reports WHERE day = ?1').bind(day).all();
    const totals = aggregate(day, results.map((r) => JSON.parse(r.body)));

    await db.prepare('INSERT INTO daily (day, totals) VALUES (?1, ?2) ON CONFLICT (day) DO NOTHING').bind(day, JSON.stringify(totals)).run();
  }

  await db.prepare('DELETE FROM reports WHERE day < ?1').bind(addDays(today, -KEEP_DAYS)).run();

  return days.map((d) => d.day);
}

export async function dailyTotals(db, fromDay) {
  const { results } = await db.prepare('SELECT totals FROM daily WHERE day >= ?1 ORDER BY day').bind(fromDay).all();

  return results.map((r) => JSON.parse(r.totals));
}
