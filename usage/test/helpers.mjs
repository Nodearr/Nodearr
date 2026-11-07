import { readFileSync } from 'node:fs';

export const example = () => JSON.parse(readFileSync(new URL('../schema/v1/example-report.json', import.meta.url), 'utf8'));

export function withId(report, n) {
  return { ...report, id: `00000000-0000-4000-8000-${String(n).padStart(12, '0')}` };
}

export function post(body, headers = { 'Content-Type': 'application/json' }) {
  return new Request('https://usage.nodearr.com/v1/report', {
    method: 'POST',
    body: typeof body === 'string' ? body : JSON.stringify(body),
    headers,
  });
}
