# usage.nodearr.com

The service that takes in Nodearr's anonymous usage reports and publishes daily totals ([ADR 0024](../docs/adr/0024-update-checks-and-usage-reports-on-nodearr-com.md), [#133](https://github.com/Nodearr/Nodearr/issues/133)). It's a [Cloudflare Worker](https://developers.cloudflare.com/workers/) with a [D1](https://developers.cloudflare.com/d1/) database. It lives beside the code that sends to it, so a change to the report changes the schema, the node and this service in one pull request.

What a report holds, and what is kept, is described for people in [Privacy and security](../docs/user-guide/privacy-and-security.md#anonymous-usage-report), which `nodearr.com/privacy` points to.

## What it does

| Request | Answer |
| --- | --- |
| `POST /v1/report` | `204` when the report matches [`schema/v1/report.schema.json`](schema/v1/report.schema.json). `400` when it doesn't, including when it has any field the schema doesn't list. `413` over 32 KB, `415` when it isn't `application/json`. |
| `GET /v1/schema.json` | The schema. |
| `GET /v1/stats.json` | The daily totals for the past year, read by `nodearr.com/stats` in the browser. Any count under 10 is `null`. |

Once a day, at 00:20 UTC, it adds up each day that's over, keeps the totals, and removes reports older than 90 days.

What it keeps:

- `reports`: one row per report ID, the latest report sent under it, and the UTC day it arrived. No address, header or time of day.
- `daily`: what each day's reports added up to.

It never reads the address a request came from, or any header but `Content-Type` and `Content-Length`. Workers Logs and traces are off in [`wrangler.toml`](wrangler.toml), and no Logpush job is set up for it. Like any host, Cloudflare sees the address while it answers.

## Testing

Node.js 22 or later.

```sh
npm ci --prefix usage
npm test --prefix usage
```

The tests run the Worker's code against SQLite through a stand-in for D1 ([`test/d1.mjs`](test/d1.mjs)). To run the Worker itself, with a local D1:

```sh
cd usage
npx wrangler d1 migrations apply nodearr-usage --local
npx wrangler dev --local
```

Then post the example: `curl -X POST -H "Content-Type: application/json" --data-binary @schema/v1/example-report.json http://127.0.0.1:8787/v1/report`.

A node can be pointed at a local Worker with `Nodearr__Usage__Url=http://127.0.0.1:8787/v1/report`, in the environment only.

## Deploying

Done once, by the owner of the Cloudflare account that holds `nodearr.com`'s DNS:

1. Sign in: `npx wrangler login`.
2. Create the database: `npx wrangler d1 create nodearr-usage`. Put the `database_id` it prints into `wrangler.toml`, in place of the zeros.
3. Create its tables: `npx wrangler d1 migrations apply nodearr-usage --remote`.
4. Deploy: `npx wrangler deploy`. The `routes` entry makes `usage.nodearr.com` the Worker's custom domain, and Cloudflare adds its DNS record. `nodearr.com` must be a zone in the same account.
5. Check it: `curl https://usage.nodearr.com/v1/schema.json`, then post the example report as above and expect `204`.
6. Check that **Workers Logs** is off for `nodearr-usage` on the Cloudflare dashboard, and that no Logpush job includes it.

After that, `npx wrangler deploy` from this folder deploys a change, and a new file in `migrations/` is applied with step 3.

## Changing the report

A new field is a change in four places, in one pull request: the schema here, `UsageReport` and `UsageReportBuilder` in `src/NzbDrone.Core/Usage/`, the field table in the privacy page, and `src/aggregate.js` if the totals should count it. Until the service is deployed with the new schema it refuses reports from nodes that send the field, so deploy it first, or bump `schema` and accept both versions.
