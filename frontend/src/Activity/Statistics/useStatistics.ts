import moment from 'moment';
import { useMemo } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export type StatisticsRange = '7days' | '30days' | '12months';

export type TransferDirection = 'fetched' | 'sent';

export type ConnectionPath = 'unknown' | 'direct' | 'relay';

export interface DailyBytes {
  day: string;
  direction: TransferDirection;
  peerNodeId: string;
  // Zero for files no Arr app could be found for.
  applicationId: number;
  bytes: number;
}

export interface DirectionTotals {
  direction: TransferDirection;
  bytes: number;
  finished: number;
  failed: number;
  averageBytesPerSecond: number;
  peakBytesPerSecond: number;
}

export interface FailureCount {
  direction: TransferDirection;
  reason: string;
  count: number;
  explanation: string;
  guideUrl: string;
}

export interface FriendStatistics {
  peerNodeId: string;
  // Null for a friend who has since been removed.
  name?: string | null;
  sent: number;
  fetched: number;
  finished: number;
  failed: number;
  checks: number;
  directChecks: number;
  route: ConnectionPath;
  routeChanged?: string | null;
}

export interface UploadQueueStatistics {
  requests: number;
  waited: number;
  averageWaitSeconds: number;
  longestWaitSeconds: number;
  maxUploads: number;
  limitMayBeLow: boolean;
}

export interface ApplicationStatistics {
  applicationId: number;
  name?: string | null;
  sharedTitles: number;
  fetchedTitles: number;
}

export interface Statistics {
  from: string;
  to: string;
  timeZone: string;
  days: string[];
  daily: DailyBytes[];
  totals: DirectionTotals[];
  failures: FailureCount[];
  friends: FriendStatistics[];
  uploadQueue: UploadQueueStatistics;
  applications: ApplicationStatistics[];
}

// The start of the range in the viewer's own time: today and the six days before it, the same
// for 30 days, or this month and the eleven before it.
export function rangeStart(range: StatisticsRange, now = moment()) {
  switch (range) {
    case '7days':
      return now.clone().startOf('day').subtract(6, 'days');
    case '12months':
      return now.clone().startOf('month').subtract(11, 'months');
    default:
      return now.clone().startOf('day').subtract(29, 'days');
  }
}

function viewerTimeZone() {
  try {
    return new Intl.DateTimeFormat().resolvedOptions().timeZone ?? '';
  } catch {
    return '';
  }
}

export default function useStatistics(range: StatisticsRange) {
  const queryParams = useMemo(
    () => ({
      from: rangeStart(range).toISOString(),
      timeZone: viewerTimeZone(),
    }),
    [range]
  );

  return useApiQuery<Statistics>({
    path: '/statistics',
    queryParams,
    queryOptions: {
      refetchInterval: 60000,
    },
  });
}
