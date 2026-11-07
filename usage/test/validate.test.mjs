import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkReport, MAX_BYTES } from '../src/validate.js';
import { example } from './helpers.mjs';

test('the example report in the schema folder is a valid report', () => {
  assert.equal(checkReport(JSON.stringify(example())).ok, true);
});

test('a report with a field the schema does not list is refused', () => {
  const report = { ...example(), hostname: 'home-server' };
  const result = checkReport(JSON.stringify(report));

  assert.equal(result.ok, false);
  assert.doesNotMatch(result.error, /home-server/, 'the refusal does not repeat what was sent');
});

test('a field added deep inside is refused too', () => {
  const report = example();
  report.network.publicAddress = '203.0.113.45:51820';

  assert.equal(checkReport(JSON.stringify(report)).ok, false);
});

test('an exact count in place of a range is refused', () => {
  const report = example();
  report.friends = '3';

  assert.equal(checkReport(JSON.stringify(report)).ok, false);
});

test('an Arr app with a URL or a full version is refused', () => {
  for (const app of [{ kind: 'sonarr', version: '4.0.17.2952' }, { kind: 'sonarr', version: '4.0', url: 'http://sonarr:8989' }]) {
    const report = example();
    report.apps = [app];

    assert.equal(checkReport(JSON.stringify(report)).ok, false, JSON.stringify(app));
  }
});

test('a failure reason the schema does not list is refused', () => {
  const report = example();
  report.past7Days.sent.failures = { '/home/owner/media': '1-5' };

  assert.equal(checkReport(JSON.stringify(report)).ok, false);
});

test('a missing field, an ID that is not random-looking, or another schema version is refused', () => {
  const withoutLanguage = example();
  delete withoutLanguage.language;

  assert.equal(checkReport(JSON.stringify(withoutLanguage)).ok, false);
  assert.equal(checkReport(JSON.stringify({ ...example(), id: 'my-node' })).ok, false);
  assert.equal(checkReport(JSON.stringify({ ...example(), schema: 2 })).ok, false);
});

test('something that is not JSON, or too large, is refused', () => {
  assert.equal(checkReport('').ok, false);
  assert.equal(checkReport('{').ok, false);
  assert.equal(checkReport('x'.repeat(MAX_BYTES + 1)).ok, false);
});
