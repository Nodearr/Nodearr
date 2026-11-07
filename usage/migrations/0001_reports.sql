-- One row per report ID: the latest report a node sent that day. No IP address, user agent or
-- time of day is kept, only the UTC day it arrived. Removed after 90 days (src/index.js).
CREATE TABLE reports (
  id TEXT PRIMARY KEY,
  day TEXT NOT NULL,
  body TEXT NOT NULL
);

CREATE INDEX reports_day ON reports (day);

-- What the reports of one UTC day add up to, kept after the reports are gone.
CREATE TABLE daily (
  day TEXT PRIMARY KEY,
  totals TEXT NOT NULL
);
