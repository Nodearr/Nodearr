import assert from 'node:assert/strict';
import { test } from 'node:test';
import worker, { handleReport, handleStats } from '../src/index.js';
import { rollUp } from '../src/store.js';
import { fakeD1 } from './d1.mjs';
import { example, post, withId } from './helpers.mjs';

const day = (iso) => new Date(`${iso}T12:00:00Z`);

async function rows(env, sql) {
  return (await env.DB.prepare(sql).all()).results;
}

test('a report is taken with 204, and kept under its ID with only the day it came', async () => {
  const env = { DB: fakeD1() };
  const request = post(example(), { 'Content-Type': 'application/json', 'CF-Connecting-IP': '198.51.100.23', 'User-Agent': 'Nodearr/0.11' });

  const response = await handleReport(request, env, day('2026-11-06'));

  assert.equal(response.status, 204);

  const kept = await rows(env, 'SELECT * FROM reports');
  assert.equal(kept.length, 1);
  assert.deepEqual(Object.keys(kept[0]).sort(), ['body', 'day', 'id']);
  assert.equal(kept[0].day, '2026-11-06');
  assert.doesNotMatch(JSON.stringify(kept), /198\.51\.100\.23|Nodearr\/0\.11/);
});

test('the latest report for an ID replaces the one before', async () => {
  const env = { DB: fakeD1() };

  await handleReport(post(example()), env, day('2026-11-06'));
  await handleReport(post({ ...example(), active: false }), env, day('2026-11-06'));

  const kept = await rows(env, 'SELECT body FROM reports');
  assert.equal(kept.length, 1);
  assert.equal(JSON.parse(kept[0].body).active, false);
});

test('a report that does not match the schema is refused with 400 and nothing is kept', async () => {
  const env = { DB: fakeD1() };

  const response = await handleReport(post({ ...example(), friendNames: ['Alice'] }), env);

  assert.equal(response.status, 400);
  assert.equal((await rows(env, 'SELECT * FROM reports')).length, 0);
});

test('only JSON, only POST, and nothing too large', async () => {
  const env = { DB: fakeD1() };

  assert.equal((await handleReport(post(example(), { 'Content-Type': 'text/plain' }), env)).status, 415);
  assert.equal((await handleReport(post(example(), { 'Content-Type': 'application/json', 'Content-Length': '100000' }), env)).status, 413);
  assert.equal((await worker.fetch(new Request('https://usage.nodearr.com/v1/report'), env)).status, 405);
  assert.equal((await worker.fetch(new Request('https://usage.nodearr.com/v1/stats.json', { method: 'DELETE' }), env)).status, 405);
});

test('the schema is published beside the endpoint', async () => {
  const response = await worker.fetch(new Request('https://usage.nodearr.com/v1/schema.json'), { DB: fakeD1() });

  assert.equal(response.status, 200);
  assert.equal((await response.json()).$id, 'https://usage.nodearr.com/v1/schema.json');
});

test('a day is added up once it is over, and the totals show counts under 10 as null', async () => {
  const env = { DB: fakeD1() };

  for (let n = 0; n < 12; n++) {
    const report = withId(example(), n);

    if (n < 3) {
      report.nodearr = { ...report.nodearr, install: 'installerTray' };
    }

    await handleReport(post(report), env, day('2026-11-05'));
  }

  await handleReport(post(withId(example(), 99)), env, day('2026-11-06'));

  assert.deepEqual(await rollUp(env.DB, '2026-11-06'), ['2026-11-05'], 'today is not added up yet');
  assert.deepEqual(await rollUp(env.DB, '2026-11-06'), [], 'running it again changes nothing');

  const stats = await (await handleStats(env, day('2026-11-06'))).json();

  assert.equal(stats.days.length, 1);

  const [totals] = stats.days;
  assert.equal(totals.day, '2026-11-05');
  assert.equal(totals.nodes, 12);
  assert.equal(totals.active, 12);
  assert.equal(totals.install.docker, null, '9 docker nodes is fewer than 10');
  assert.equal(totals.install.installerTray, null);
  assert.equal(totals.apps['sonarr 4.0'], 12);
  assert.equal(totals.routes.someRelayOnly, 12);
  assert.equal(totals.failures.NoDirectRoute, 12);
});

test('the totals can be read from nodearr.com in the browser', async () => {
  const response = await worker.fetch(new Request('https://usage.nodearr.com/v1/stats.json'), { DB: fakeD1() });

  assert.equal(response.headers.get('Access-Control-Allow-Origin'), 'https://nodearr.com');
  assert.deepEqual((await response.json()).days, []);
});

test('reports older than 90 days are removed, and their totals stay', async () => {
  const env = { DB: fakeD1() };

  await handleReport(post(example()), env, day('2026-08-01'));
  await rollUp(env.DB, '2026-08-02');
  await rollUp(env.DB, '2026-11-06');

  assert.equal((await rows(env, 'SELECT * FROM reports')).length, 0);
  assert.equal((await rows(env, 'SELECT * FROM daily')).length, 1);
});

test('the daily run adds up the day before', async () => {
  const env = { DB: fakeD1() };

  await handleReport(post(example()), env, day('2026-11-05'));
  await worker.scheduled({ scheduledTime: Date.parse('2026-11-06T00:20:00Z') }, env);

  assert.equal((await rows(env, 'SELECT day FROM daily'))[0].day, '2026-11-05');
});
