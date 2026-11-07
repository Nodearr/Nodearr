import assert from 'node:assert/strict';
import { test } from 'node:test';
import { aggregate, publishable } from '../src/aggregate.js';
import { example, withId } from './helpers.mjs';

test('a node counts once for an app version, however many of that app it has', () => {
  const report = example();
  report.apps = [{ kind: 'sonarr', version: '4.0' }, { kind: 'sonarr', version: '4.0' }];

  assert.equal(aggregate('2026-11-05', [report]).apps['sonarr 4.0'], 1);
});

test('nodes whose friends were all reached directly are told apart from those with a relay-only friend', () => {
  const direct = withId(example(), 1);
  direct.network = { ...direct.network, friendsRelayOnly: '0' };
  const relay = withId(example(), 2);

  const totals = aggregate('2026-11-05', [direct, relay]);

  assert.equal(totals.routes.allDirect, 1);
  assert.equal(totals.routes.someRelayOnly, 1);
});

test('published totals hide every count under 10 and keep the rest', () => {
  assert.deepEqual(
    publishable({ day: '2026-11-05', nodes: 10, install: { docker: 9, zip: 25 } }),
    { day: '2026-11-05', nodes: 10, install: { docker: null, zip: 25 } },
  );
});
