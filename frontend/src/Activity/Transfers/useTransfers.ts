import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export type TransferState = 'queued' | 'downloading' | 'completed' | 'failed';

export interface Transfer {
  id: number;
  downloadId: string;
  title: string;
  mediaKind: 'tv' | 'movie' | 'music';
  category?: string;
  peerId: number;
  peerName?: string;
  state: TransferState;
  failure: string;
  failureMessage?: string;
  size: number;
  bytesDone: number;
  attempts: number;
  nextAttempt?: string;
  outputPath?: string;
  added: string;
  started?: string;
  ended?: string;
  // Downloading, and waiting its turn at the friend's node: the place in line it gave.
  waitingPosition?: number | null;
}

export type UploadState =
  | 'waiting'
  | 'preparing'
  | 'sending'
  | 'completed'
  | 'failed';

// One file this node is sending, or has sent, to another node.
export interface Upload {
  id: string;
  title: string;
  peerName?: string;
  state: UploadState;
  failure?: string;
  failureMessage?: string;
  attempts: number;
  lastFailure?: string;
  lastFailureMessage?: string;
  // Waiting for a free upload slot: its place in line.
  queuePosition?: number | null;
  size: number;
  bytesSent: number;
  started: string;
  ended?: string;
}

export const TRANSFERS_PATH = '/transfers';
export const UPLOADS_PATH = '/transfers/uploads';

// SignalR invalidates this query when a transfer changes. The Transfers page retains a slow
// fallback poll; the always-mounted sidebar observes the same query without adding a timer.
export const useTransfers = (refetchInterval?: number) =>
  useApiQuery<Transfer[]>({
    path: TRANSFERS_PATH,
    queryOptions: { refetchInterval },
  });

export const useUploads = () =>
  useApiQuery<Upload[]>({
    path: UPLOADS_PATH,
    queryOptions: { refetchInterval: 15000 },
  });

export type TransferAction = 'remove' | 'retry';

export const useTransferAction = () => {
  const queryClient = useQueryClient();

  return useMutation<
    unknown,
    ApiError,
    { downloadId: string; action: TransferAction }
  >({
    mutationFn: ({ downloadId, action }) =>
      fetchJson<unknown, unknown>({
        path: getQueryPath(
          action === 'retry'
            ? `${TRANSFERS_PATH}/${downloadId}/retry`
            : `${TRANSFERS_PATH}/${downloadId}`
        ),
        method: action === 'retry' ? 'POST' : 'DELETE',
        headers: {
          'X-Api-Key': window.Nodearr.apiKey,
          'X-Nodearr-Client': 'Nodearr',
        },
      }),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [TRANSFERS_PATH] });
    },
  });
};
