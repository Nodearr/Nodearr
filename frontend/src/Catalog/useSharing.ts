import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export type MediaKind = 'tv' | 'movie';

export interface TitleKey {
  mediaKind: MediaKind;
  applicationId: number;
  arrId: number;
}

export interface ShareableTitle extends TitleKey {
  applicationName?: string;
  title: string;
  year: number;
  files: number;
}

export interface ShareList {
  id: number;
  name: string;
  isHidden: boolean;
  items: TitleKey[];
  peerIds: number[];
}

export type PeerItemStatus = 'notInLibrary' | 'missing' | 'upgrade' | 'have';

export interface PeerCatalogSummary {
  peerId: number;
  peerName: string;
  catalogVersion?: string;
  catalogSyncedAt?: string;
  catalogError?: string;
  notInLibrary: number;
  missing: number;
  upgrade: number;
  have: number;
}

export interface PeerCatalogItem {
  id: number;
  peerId: number;
  peerName?: string;
  mediaKind: MediaKind;
  localStatus: PeerItemStatus;
  title?: string;
  year: number;
  seasonNumber: number;
  episodeNumbers: number[];
  episodeTitles?: string;
  releaseTitle?: string;
  quality?: string;
  resolution: number;
  size: number;
  dateAdded?: string;
}

const LISTS_PATH = '/sharing/lists';
const TITLES_PATH = '/sharing/titles';
const PEERS_PATH = '/nodes/peers';

export function titleId(key: TitleKey) {
  return `${key.mediaKind}:${key.applicationId}:${key.arrId}`;
}

function send<T>(
  method: 'POST' | 'PUT' | 'DELETE',
  path: string,
  body?: unknown
) {
  return fetchJson<T, unknown>({
    path: getQueryPath(path),
    method,
    body,
    headers: {
      'X-Api-Key': window.Nodearr.apiKey,
      'X-Nodearr-Client': 'Nodearr',
    },
  });
}

export const useShareLists = () =>
  useApiQuery<ShareList[]>({ path: LISTS_PATH });

export const useShareableTitles = () =>
  useApiQuery<ShareableTitle[]>({ path: TITLES_PATH });

export const usePeerCatalogSummaries = () =>
  useApiQuery<PeerCatalogSummary[]>({ path: '/catalog/peers' });

export const useSaveShareList = () => {
  const queryClient = useQueryClient();

  return useMutation<ShareList, ApiError, Partial<ShareList>>({
    mutationFn: (list) =>
      list.id
        ? send<ShareList>('PUT', `${LISTS_PATH}/${list.id}`, list)
        : send<ShareList>('POST', LISTS_PATH, list),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [LISTS_PATH] });
    },
  });
};

export const useDeleteShareList = () => {
  const queryClient = useQueryClient();

  return useMutation<unknown, ApiError, number>({
    mutationFn: (id) => send('DELETE', `${LISTS_PATH}/${id}`),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [LISTS_PATH] });
    },
  });
};

export const useSetShareAll = () => {
  const queryClient = useQueryClient();

  return useMutation<unknown, ApiError, { id: number; shareAll: boolean }>({
    mutationFn: ({ id, shareAll }) =>
      send('PUT', `${PEERS_PATH}/${id}/shareall`, { shareAll }),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [PEERS_PATH] });
    },
  });
};
