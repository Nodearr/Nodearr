import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export type MediaKind = 'tv' | 'movie' | 'music';

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

// A whole series or movie, one season of a series, or one episode of it, the way
// Sonarr monitors them. arrId is the series or the movie.
export interface ShareKey extends TitleKey {
  seasonNumber?: number | null;
  episodeArrId?: number | null;
}

export interface ShareableEpisode {
  episodeArrId: number;
  episodeNumber: number;
  title?: string;
  hasFile: boolean;
}

export interface ShareableSeason {
  seasonNumber: number;
  episodes: ShareableEpisode[];
}

export interface ShareList {
  id: number;
  name: string;
  isHidden: boolean;
  items: ShareKey[];
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
  artist?: string;
  trackCount: number;
  releaseTitle?: string;
  quality?: string;
  resolution: number;
  size: number;
  dateAdded?: string;
}

export interface HashingStatus {
  shared: number;
  hashed: number;
  current?: string;
  waitingForUploads: boolean;
  lastPass?: string;
}

const LISTS_PATH = '/sharing/lists';
const TITLES_PATH = '/sharing/titles';
const PEERS_PATH = '/nodes/peers';

export function titleId(key: TitleKey) {
  return `${key.mediaKind}:${key.applicationId}:${key.arrId}`;
}

export function seasonShareId(title: TitleKey, seasonNumber: number) {
  return `${titleId(title)}:s${seasonNumber}`;
}

export function episodeShareId(title: TitleKey, episodeArrId: number) {
  return `${titleId(title)}:e${episodeArrId}`;
}

// One string for each thing a list can hold, so a list can be kept as a Set.
export function shareId(key: ShareKey) {
  if (key.episodeArrId != null) {
    return episodeShareId(key, key.episodeArrId);
  }

  if (key.seasonNumber != null) {
    return seasonShareId(key, key.seasonNumber);
  }

  return titleId(key);
}

export function parseShareId(id: string): ShareKey {
  const [mediaKind, applicationId, arrId, part] = id.split(':');
  const key: ShareKey = {
    mediaKind: mediaKind as MediaKind,
    applicationId: Number(applicationId),
    arrId: Number(arrId),
  };

  if (part?.startsWith('s')) {
    key.seasonNumber = Number(part.slice(1));
  } else if (part?.startsWith('e')) {
    key.episodeArrId = Number(part.slice(1));
  }

  return key;
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

// Read only when the series is opened on the page.
export const useShareableSeasons = (title: TitleKey, enabled: boolean) =>
  useApiQuery<ShareableSeason[]>({
    path: `${TITLES_PATH}/${title.applicationId}/${title.arrId}/seasons`,
    queryOptions: { enabled },
  });

export const useHashingStatus = () =>
  useApiQuery<HashingStatus>({
    path: '/sharing/hashing',
    queryOptions: { refetchInterval: 5000 },
  });

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
