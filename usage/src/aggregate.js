// What one UTC day's reports add up to. Every number is a count of nodes, that is of report IDs,
// since the ingest keeps one report per ID and an ID lasts one day.

// Published totals show no count under this: "fewer than 10".
export const SMALLEST_SHOWN = 10;

function add(map, key, by = 1) {
  map[key] = (map[key] ?? 0) + by;
}

function sorted(map) {
  return Object.fromEntries(Object.entries(map).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
}

function tri(value) {
  return value === true ? 'yes' : value === false ? 'no' : 'unknown';
}

export function aggregate(day, reports) {
  const totals = {
    day,
    nodes: reports.length,
    active: 0,
    version: {},
    branch: {},
    platform: {},
    runtime: {},
    database: {},
    install: {},
    apps: {},
    friends: {},
    routes: { allDirect: 0, someRelayOnly: 0, someUnreachable: 0, noneChecked: 0 },
    failures: {},
    withFailures: 0,
    transfers: { sent: 0, fetched: 0 },
    udp: {},
    portVaries: {},
    relay: {},
    dockerDesktop: 0,
    features: {},
    maxUploads: {},
    language: {},
  };

  for (const r of reports) {
    if (r.active) totals.active += 1;

    add(totals.version, r.nodearr.version);
    add(totals.branch, r.nodearr.branch);
    add(totals.platform, r.nodearr.platform);
    add(totals.runtime, r.nodearr.runtime.split('.').slice(0, 2).join('.'));
    add(totals.database, r.nodearr.database);
    add(totals.install, r.nodearr.install);

    // A node with two Sonarrs of one version counts once for it.
    for (const app of new Set(r.apps.map((a) => `${a.kind} ${a.version}`))) {
      add(totals.apps, app);
    }

    add(totals.friends, r.friends);

    // How often the direct-only rule bites: friends a node could reach only through a relay.
    const n = r.network;

    if (n.friendsRelayOnly !== '0') totals.routes.someRelayOnly += 1;
    if (n.friendsUnreachable !== '0') totals.routes.someUnreachable += 1;
    if (n.friendsDirect !== '0' && n.friendsRelayOnly === '0' && n.friendsUnreachable === '0') totals.routes.allDirect += 1;
    if (n.friendsDirect === '0' && n.friendsRelayOnly === '0' && n.friendsUnreachable === '0') totals.routes.noneChecked += 1;

    const week = r.past7Days;
    const reasons = new Set([...Object.keys(week.sent.failures), ...Object.keys(week.fetched.failures)]);

    for (const reason of reasons) add(totals.failures, reason);
    if (reasons.size > 0) totals.withFailures += 1;
    if (week.sent.finished !== '0') totals.transfers.sent += 1;
    if (week.fetched.finished !== '0') totals.transfers.fetched += 1;

    add(totals.udp, tri(n.udp));
    add(totals.portVaries, tri(n.portVaries));
    add(totals.relay, n.relay);
    if (n.dockerDesktop) totals.dockerDesktop += 1;

    for (const [feature, on] of Object.entries(r.features)) {
      if (feature === 'maxUploads') {
        add(totals.maxUploads, on);
      } else if (on) {
        add(totals.features, feature);
      } else {
        totals.features[feature] ??= 0;
      }
    }

    add(totals.language, r.language);
  }

  for (const key of ['version', 'branch', 'platform', 'runtime', 'database', 'install', 'apps', 'friends', 'failures', 'udp', 'portVaries', 'relay', 'features', 'maxUploads', 'language']) {
    totals[key] = sorted(totals[key]);
  }

  return totals;
}

// The totals as published: any count under SMALLEST_SHOWN becomes null, so a node that is one of
// only a few with some combination can't be picked out. The day itself is kept.
export function publishable(totals) {
  const hide = (value) => {
    if (typeof value === 'number') return value < SMALLEST_SHOWN ? null : value;
    if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, hide(v)]));
    return value;
  };

  const { day, ...counts } = totals;

  return { day, ...hide(counts) };
}
