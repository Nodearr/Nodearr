// A stand-in for a D1 binding over sql.js, an in-memory SQLite, with the part of D1's API the
// service uses: prepare, bind, run, all and first. D1 is SQLite, so the SQL is checked as written.
import { readFileSync } from 'node:fs';
import initSqlJs from 'sql.js';

const SQL = await initSqlJs();

export function fakeD1() {
  const db = new SQL.Database();
  db.exec(readFileSync(new URL('../migrations/0001_reports.sql', import.meta.url), 'utf8'));

  const statement = (sql, params = []) => ({
    bind: (...values) => statement(sql, values),
    async run() {
      db.run(sql, params);
      return { success: true };
    },
    async all() {
      const stmt = db.prepare(sql);
      stmt.bind(params);
      const results = [];
      while (stmt.step()) results.push(stmt.getAsObject());
      stmt.free();
      return { results, success: true };
    },
    async first() {
      return (await this.all()).results[0] ?? null;
    },
  });

  return { prepare: (sql) => statement(sql) };
}
