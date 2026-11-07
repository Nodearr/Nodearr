// usage.nodearr.com (ADR 0024). Write-only for reports: a node can send one, and nothing can be
// read back except the daily totals that nodearr.com/stats shows.
//
// It never reads the address a request came from, or any header but Content-Type and
// Content-Length, and stores nothing about the request: a report is kept under its own random
// ID, with the UTC day it arrived.

import { publishable } from './aggregate.js';
import { addDays, dailyTotals, KEEP_DAYS, putReport, rollUp, utcDay } from './store.js';
import { checkReport, MAX_BYTES, schema } from './validate.js';

// The website reads the totals from the browser.
const STATS_ORIGIN = 'https://nodearr.com';

// Totals are published for this many days back.
const PUBLISHED_DAYS = 365;

function text(status, body, headers = {}) {
  return new Response(body, { status, headers: { 'Content-Type': 'text/plain; charset=utf-8', ...headers } });
}

function json(body, headers = {}) {
  return new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json; charset=utf-8', ...headers } });
}

export async function handleReport(request, env, now = new Date()) {
  if (!(request.headers.get('Content-Type') ?? '').toLowerCase().startsWith('application/json')) {
    return text(415, 'Send the report as application/json.');
  }

  const length = Number(request.headers.get('Content-Length') ?? '0');

  if (length > MAX_BYTES) {
    return text(413, `A report is at most ${MAX_BYTES} bytes.`);
  }

  const checked = checkReport(await request.text());

  if (!checked.ok) {
    return text(400, checked.error);
  }

  await putReport(env.DB, checked.report, utcDay(now));

  return new Response(null, { status: 204 });
}

export async function handleStats(env, now = new Date()) {
  const days = await dailyTotals(env.DB, addDays(utcDay(now), -PUBLISHED_DAYS));

  return json(
    { schema: 1, smallestShown: 10, days: days.map(publishable) },
    {
      'Access-Control-Allow-Origin': STATS_ORIGIN,
      'Cache-Control': 'public, max-age=3600',
    },
  );
}

export default {
  async fetch(request, env) {
    const { pathname } = new URL(request.url);

    if (pathname === '/v1/report') {
      return request.method === 'POST' ? handleReport(request, env) : text(405, 'Reports are sent with POST.', { Allow: 'POST' });
    }

    if (request.method !== 'GET' && request.method !== 'HEAD') {
      return text(405, 'Only reports are sent here.', { Allow: 'GET, HEAD' });
    }

    if (pathname === '/v1/schema.json') {
      return json(schema, { 'Access-Control-Allow-Origin': '*', 'Cache-Control': 'public, max-age=3600' });
    }

    if (pathname === '/v1/stats.json') {
      return handleStats(env);
    }

    if (pathname === '/') {
      return text(200, `usage.nodearr.com takes in Nodearr's anonymous usage reports. What they hold and what is kept: https://nodearr.com/privacy. The totals: https://nodearr.com/stats. Raw reports are kept ${KEEP_DAYS} days.`);
    }

    return text(404, 'Not found.');
  },

  async scheduled(controller, env) {
    await rollUp(env.DB, utcDay(new Date(controller.scheduledTime)));
  },
};
