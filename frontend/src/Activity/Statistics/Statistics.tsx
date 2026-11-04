import moment from 'moment';
import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import formatTimeSpan from 'Utilities/Date/formatTimeSpan';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import BytesChart, { ChartSeries } from './BytesChart';
import useStatistics, {
  ConnectionPath,
  DirectionTotals,
  Statistics as StatisticsReport,
  StatisticsRange,
  TransferDirection,
} from './useStatistics';
import styles from './Statistics.css';

type Split = 'friend' | 'app';

const RANGES: { range: StatisticsRange; label: string }[] = [
  { range: '7days', label: 'StatisticsRange7Days' },
  { range: '30days', label: 'StatisticsRange30Days' },
  { range: '12months', label: 'StatisticsRange12Months' },
];

const DIRECTIONS: { direction: TransferDirection; label: string }[] = [
  { direction: 'fetched', label: 'StatisticsFetched' },
  { direction: 'sent', label: 'StatisticsSent' },
];

// The chart's series colors, in the order friends and apps are listed. Any past these share
// one color as Other, so a friend's color never depends on how much they moved.
const SERIES_COLORS = [
  'chartColor1',
  'chartColor2',
  'chartColor3',
  'chartColor4',
  'chartColor5',
  'chartColor6',
  'chartColor7',
  'chartColor8',
];

const OTHER = 'other';

const ROUTE_LABELS: Record<ConnectionPath, string> = {
  direct: 'StatisticsRouteDirect',
  relay: 'StatisticsRouteRelay',
  unknown: 'StatisticsRouteUnknown',
};

const FAILURE_COLUMNS: Column[] = [
  {
    name: 'direction',
    label: () => translate('StatisticsDirection'),
    isVisible: true,
  },
  {
    name: 'reason',
    label: () => translate('StatisticsReason'),
    isVisible: true,
  },
  {
    name: 'count',
    label: () => translate('StatisticsTransfers'),
    isVisible: true,
  },
  {
    name: 'explanation',
    label: () => translate('StatisticsWhatItMeans'),
    isVisible: true,
  },
];

const FRIEND_COLUMNS: Column[] = [
  { name: 'name', label: () => translate('StatisticsFriend'), isVisible: true },
  { name: 'sent', label: () => translate('StatisticsSent'), isVisible: true },
  {
    name: 'fetched',
    label: () => translate('StatisticsFetched'),
    isVisible: true,
  },
  {
    name: 'finished',
    label: () => translate('StatisticsFinished'),
    isVisible: true,
  },
  {
    name: 'failed',
    label: () => translate('StatisticsFailed'),
    isVisible: true,
  },
  {
    name: 'direct',
    label: () => translate('StatisticsDirectChecks'),
    isVisible: true,
  },
  { name: 'route', label: () => translate('StatisticsRoute'), isVisible: true },
  {
    name: 'routeChanged',
    label: () => translate('StatisticsRouteChanged'),
    isVisible: true,
  },
];

const APPLICATION_COLUMNS: Column[] = [
  { name: 'name', label: () => translate('StatisticsApp'), isVisible: true },
  {
    name: 'shared',
    label: () => translate('StatisticsTitlesShared'),
    isVisible: true,
  },
  {
    name: 'fetched',
    label: () => translate('StatisticsTitlesFetched'),
    isVisible: true,
  },
];

function formatSpeed(bytesPerSecond: number) {
  return translate('StatisticsBytesPerSecond', {
    size: formatBytes(bytesPerSecond),
  });
}

function formatWait(seconds: number) {
  return seconds > 0 ? formatTimeSpan(Math.round(seconds) * 1000) : '00:00:00';
}

function friendName(name: string | null | undefined) {
  return name ?? translate('StatisticsRemovedFriend');
}

// Twelve months of days are too many bars to read, so that range goes by month.
function bucketOf(day: string, byMonth: boolean) {
  return byMonth ? day.slice(0, 7) : day;
}

function bucketLabel(bucket: string, byMonth: boolean) {
  return byMonth
    ? moment(bucket, 'YYYY-MM').format('MMM YYYY')
    : moment(bucket, 'YYYY-MM-DD').format('MMM D');
}

interface SeriesEntity {
  key: string;
  label: string;
}

interface ChartData {
  labels: string[];
  byDirection: Record<TransferDirection, ChartSeries[]>;
  max: number;
}

function buildChart(
  report: StatisticsReport,
  split: Split,
  byMonth: boolean
): ChartData {
  const buckets = Array.from(
    new Set(report.days.map((day) => bucketOf(day, byMonth)))
  );
  const bucketIndex = new Map(buckets.map((bucket, i) => [bucket, i]));

  const entities: SeriesEntity[] =
    split === 'friend'
      ? report.friends.map((f) => ({
          key: f.peerNodeId,
          label: friendName(f.name),
        }))
      : report.applications.map((a) => ({
          key: String(a.applicationId),
          label:
            a.applicationId === 0
              ? translate('StatisticsNoApp')
              : a.name ?? translate('StatisticsRemovedApp'),
        }));

  const slots = new Map(
    entities
      .slice(0, SERIES_COLORS.length)
      .map((entity, i) => [entity.key, i] as const)
  );

  const byDirection = {} as Record<TransferDirection, ChartSeries[]>;
  const totals = buckets.map(() => ({ fetched: 0, sent: 0 }));

  DIRECTIONS.forEach(({ direction }) => {
    const series = new Map<string, ChartSeries>();

    report.daily
      .filter((d) => d.direction === direction)
      .forEach((d) => {
        const entityKey =
          split === 'friend' ? d.peerNodeId : String(d.applicationId);
        const slot = slots.get(entityKey);
        const key = slot === undefined ? OTHER : entityKey;
        const index = bucketIndex.get(bucketOf(d.day, byMonth));

        if (index === undefined) {
          return;
        }

        let s = series.get(key);

        if (!s) {
          s = {
            key,
            label:
              slot === undefined
                ? translate('StatisticsOther')
                : entities[slot].label,
            color: slot === undefined ? 'chartOtherColor' : SERIES_COLORS[slot],
            values: buckets.map(() => 0),
          };
          series.set(key, s);
        }

        s.values[index] += d.bytes;
        totals[index][direction] += d.bytes;
      });

    // In the order friends and apps are listed, with Other last.
    byDirection[direction] = Array.from(series.values()).sort(
      (a, b) =>
        (slots.get(a.key) ?? SERIES_COLORS.length) -
        (slots.get(b.key) ?? SERIES_COLORS.length)
    );
  });

  return {
    labels: buckets.map((bucket) => bucketLabel(bucket, byMonth)),
    byDirection,
    max: Math.max(0, ...totals.map((t) => Math.max(t.fetched, t.sent))),
  };
}

function Totals({ totals }: { totals: DirectionTotals }) {
  return (
    <div className={styles.stats}>
      <div className={styles.stat}>
        <span className={styles.statValue}>{formatBytes(totals.bytes)}</span>
        <span className={styles.statLabel}>{translate('StatisticsData')}</span>
      </div>
      <div className={styles.stat}>
        <span className={styles.statValue}>{totals.finished}</span>
        <span className={styles.statLabel}>
          {translate('StatisticsFinished')}
        </span>
      </div>
      <div className={styles.stat}>
        <span className={styles.statValue}>{totals.failed}</span>
        <span className={styles.statLabel}>
          {translate('StatisticsFailed')}
        </span>
      </div>
      <div className={styles.stat}>
        <span className={styles.statValue}>
          {formatSpeed(totals.averageBytesPerSecond)}
        </span>
        <span className={styles.statLabel}>
          {translate('StatisticsAverageSpeed')}
        </span>
      </div>
      <div className={styles.stat}>
        <span className={styles.statValue}>
          {formatSpeed(totals.peakBytesPerSecond)}
        </span>
        <span className={styles.statLabel}>
          {translate('StatisticsPeakSpeed')}
        </span>
      </div>
    </div>
  );
}

function ChartTable({
  labels,
  series,
}: {
  labels: string[];
  series: ChartSeries[];
}) {
  const columns: Column[] = [
    {
      name: 'bucket',
      label: () => translate('StatisticsWhen'),
      isVisible: true,
    },
    ...series.map((s) => ({ name: s.key, label: s.label, isVisible: true })),
  ];

  return (
    <Table columns={columns} canModifyColumns={false}>
      <TableBody>
        {labels.map((label, i) =>
          series.some((s) => s.values[i] > 0) ? (
            <TableRow key={label}>
              <TableRowCell>{label}</TableRowCell>
              {series.map((s) => (
                <TableRowCell key={s.key}>
                  {formatBytes(s.values[i])}
                </TableRowCell>
              ))}
            </TableRow>
          ) : null
        )}
      </TableBody>
    </Table>
  );
}

interface RangeButtonProps {
  range: StatisticsRange;
  label: string;
  isSelected: boolean;
  onPress: (range: StatisticsRange) => void;
}

function RangeButton({ range, label, isSelected, onPress }: RangeButtonProps) {
  const handlePress = useCallback(() => onPress(range), [range, onPress]);

  return (
    <Button
      kind={isSelected ? kinds.PRIMARY : kinds.DEFAULT}
      onPress={handlePress}
    >
      {translate(label)}
    </Button>
  );
}

function Statistics() {
  const [range, setRange] = useState<StatisticsRange>('30days');
  const [split, setSplit] = useState<Split>('friend');
  const [showTable, setShowTable] = useState(false);
  const { data: report, isLoading, error } = useStatistics(range);

  const byMonth = range === '12months';
  const chart = useMemo(
    () => (report ? buildChart(report, split, byMonth) : null),
    [report, split, byMonth]
  );

  const handleSplitByFriend = useCallback(() => setSplit('friend'), []);
  const handleSplitByApp = useCallback(() => setSplit('app'), []);
  const handleToggleTable = useCallback(() => setShowTable((t) => !t), []);

  const queue = report?.uploadQueue;

  return (
    <PageContent title={translate('Statistics')}>
      <PageContentBody>
        <Alert kind={kinds.INFO}>{translate('StatisticsHelpText')}</Alert>

        <div className={styles.controls}>
          <div className={styles.buttons}>
            {RANGES.map((r) => (
              <RangeButton
                key={r.range}
                range={r.range}
                label={r.label}
                isSelected={range === r.range}
                onPress={setRange}
              />
            ))}
          </div>
        </div>

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('StatisticsLoadError')}</Alert>
        ) : null}

        {report && chart ? (
          <>
            <div className={styles.directions}>
              {DIRECTIONS.map(({ direction, label }) => {
                const totals = report.totals.find(
                  (t) => t.direction === direction
                );

                return totals ? (
                  <FieldSet key={direction} legend={translate(label)}>
                    <Totals totals={totals} />
                  </FieldSet>
                ) : null;
              })}
            </div>
            <div className={styles.muted}>
              {translate('StatisticsSpeedHelpText')}
            </div>

            <FieldSet
              legend={translate(
                byMonth ? 'StatisticsDataPerMonth' : 'StatisticsDataPerDay'
              )}
            >
              <div className={styles.buttons}>
                <Button
                  kind={split === 'friend' ? kinds.PRIMARY : kinds.DEFAULT}
                  onPress={handleSplitByFriend}
                >
                  {translate('StatisticsSplitByFriend')}
                </Button>
                <Button
                  kind={split === 'app' ? kinds.PRIMARY : kinds.DEFAULT}
                  onPress={handleSplitByApp}
                >
                  {translate('StatisticsSplitByApp')}
                </Button>
                <Button onPress={handleToggleTable}>
                  {translate(
                    showTable ? 'StatisticsShowChart' : 'StatisticsShowTable'
                  )}
                </Button>
              </div>

              {report.daily.length === 0 ? (
                <div className={styles.muted}>
                  {translate('StatisticsNothingMoved')}
                </div>
              ) : (
                <div className={styles.charts}>
                  {DIRECTIONS.map(({ direction, label }) =>
                    showTable ? (
                      <div key={direction} className={styles.chartTable}>
                        <div className={styles.chartTitle}>
                          {translate(label)}
                        </div>
                        <ChartTable
                          labels={chart.labels}
                          series={chart.byDirection[direction]}
                        />
                      </div>
                    ) : (
                      <BytesChart
                        key={direction}
                        title={translate(label)}
                        labels={chart.labels}
                        series={chart.byDirection[direction]}
                        max={chart.max}
                      />
                    )
                  )}
                </div>
              )}
            </FieldSet>

            <FieldSet legend={translate('StatisticsFailures')}>
              {report.failures.length === 0 ? (
                <div className={styles.muted}>
                  {translate('StatisticsNoFailures')}
                </div>
              ) : (
                <Table columns={FAILURE_COLUMNS} canModifyColumns={false}>
                  <TableBody>
                    {report.failures.map((f) => (
                      <TableRow key={`${f.direction}-${f.reason}`}>
                        <TableRowCell>
                          {translate(
                            f.direction === 'sent'
                              ? 'StatisticsSent'
                              : 'StatisticsFetched'
                          )}
                        </TableRowCell>
                        <TableRowCell className={styles.reason}>
                          {f.reason}
                        </TableRowCell>
                        <TableRowCell>{f.count}</TableRowCell>
                        <TableRowCell className={styles.explanation}>
                          {f.explanation}{' '}
                          <Link to={f.guideUrl} target="_blank">
                            {translate('StatisticsTroubleshooting')}
                          </Link>
                        </TableRowCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </FieldSet>

            <FieldSet legend={translate('StatisticsFriends')}>
              {report.friends.length === 0 ? (
                <div className={styles.muted}>
                  {translate('StatisticsNoFriends')}
                </div>
              ) : (
                <Table columns={FRIEND_COLUMNS} canModifyColumns={false}>
                  <TableBody>
                    {report.friends.map((f) => (
                      <TableRow key={f.peerNodeId}>
                        <TableRowCell>{friendName(f.name)}</TableRowCell>
                        <TableRowCell>{formatBytes(f.sent)}</TableRowCell>
                        <TableRowCell>{formatBytes(f.fetched)}</TableRowCell>
                        <TableRowCell>{f.finished}</TableRowCell>
                        <TableRowCell>{f.failed}</TableRowCell>
                        <TableRowCell>
                          {f.checks > 0
                            ? translate('StatisticsDirectChecksValue', {
                                percent: Math.round(
                                  (f.directChecks / f.checks) * 100
                                ),
                                checks: f.checks,
                              })
                            : translate('StatisticsNoChecks')}
                        </TableRowCell>
                        <TableRowCell>
                          {translate(ROUTE_LABELS[f.route])}
                        </TableRowCell>
                        {f.routeChanged ? (
                          <RelativeDateCell date={f.routeChanged} />
                        ) : (
                          <TableRowCell>
                            {translate('StatisticsRouteNeverChanged')}
                          </TableRowCell>
                        )}
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </FieldSet>

            {queue ? (
              <FieldSet legend={translate('StatisticsUploadQueue')}>
                {queue.limitMayBeLow ? (
                  <Alert kind={kinds.WARNING}>
                    {translate('StatisticsLimitMayBeLow', {
                      waited: queue.waited,
                      requests: queue.requests,
                      limit: queue.maxUploads,
                    })}{' '}
                    <Link to="/settings/node">
                      {translate('StatisticsOpenNodeSettings')}
                    </Link>
                  </Alert>
                ) : null}

                <div className={styles.stats}>
                  <div className={styles.stat}>
                    <span className={styles.statValue}>{queue.requests}</span>
                    <span className={styles.statLabel}>
                      {translate('StatisticsRequests')}
                    </span>
                  </div>
                  <div className={styles.stat}>
                    <span className={styles.statValue}>{queue.waited}</span>
                    <span className={styles.statLabel}>
                      {translate('StatisticsWaited')}
                    </span>
                  </div>
                  <div className={styles.stat}>
                    <span className={styles.statValue}>
                      {formatWait(queue.averageWaitSeconds)}
                    </span>
                    <span className={styles.statLabel}>
                      {translate('StatisticsAverageWait')}
                    </span>
                  </div>
                  <div className={styles.stat}>
                    <span className={styles.statValue}>
                      {formatWait(queue.longestWaitSeconds)}
                    </span>
                    <span className={styles.statLabel}>
                      {translate('StatisticsLongestWait')}
                    </span>
                  </div>
                </div>
              </FieldSet>
            ) : null}

            <FieldSet legend={translate('StatisticsCatalog')}>
              <div className={styles.muted}>
                {translate('StatisticsCatalogHelpText')}
              </div>
              <Table columns={APPLICATION_COLUMNS} canModifyColumns={false}>
                <TableBody>
                  {report.applications.map((a) => (
                    <TableRow key={a.applicationId}>
                      <TableRowCell>
                        {a.applicationId === 0
                          ? translate('StatisticsNoApp')
                          : a.name ?? translate('StatisticsRemovedApp')}
                      </TableRowCell>
                      <TableRowCell>{a.sharedTitles}</TableRowCell>
                      <TableRowCell>{a.fetchedTitles}</TableRowCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </FieldSet>
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default Statistics;
