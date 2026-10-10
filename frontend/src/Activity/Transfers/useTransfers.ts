import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export type TransferState = 'queued' | 'downloading' | 'completed' | 'failed';

export interface Transfer {
  id: number;
  downloadId: string;
  title: string;
  mediaKind: 'tv' | 'movie';
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
}

export const TRANSFERS_PATH = '/transfers';

// The list is fetched again whenever the server says a transfer moved on (see SignalRListener).
// The slow poll is only there in case that message is missed.
export const useTransfers = () =>
  useApiQuery<Transfer[]>({
    path: TRANSFERS_PATH,
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
