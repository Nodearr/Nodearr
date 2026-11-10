import { Validator } from '@cfworker/json-schema';
import schema from '../schema/v1/report.schema.json' with { type: 'json' };

// A report is a few kilobytes. Anything much larger isn't one.
export const MAX_BYTES = 32 * 1024;

const validator = new Validator(schema, '2020-12', false);

export { schema };

// Parses and checks a report against the published schema. A report with any field the schema
// doesn't list is refused whole: nothing unknown is ever stored.
export function checkReport(text) {
  if (typeof text !== 'string' || text.length === 0) {
    return { ok: false, error: 'The body is empty.' };
  }

  if (new TextEncoder().encode(text).length > MAX_BYTES) {
    return { ok: false, error: `The body is larger than ${MAX_BYTES} bytes.` };
  }

  let report;

  try {
    report = JSON.parse(text);
  } catch {
    return { ok: false, error: 'The body is not JSON.' };
  }

  const result = validator.validate(report);

  if (!result.valid) {
    // Says where, not what: the value may be something that should never have been sent.
    const where = [...new Set(result.errors.map((e) => e.instanceLocation))].slice(0, 5);

    return { ok: false, error: `The report does not match the schema at ${where.join(', ')}.` };
  }

  return { ok: true, report };
}
