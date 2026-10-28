import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import type { Peer } from 'Nodes/useNodes';
import Application from 'typings/Application';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import translate from 'Utilities/String/translate';

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

// A whole series or movie, one season of a series, or one episode of it, a whole
// artist, or one album of it, the way the Arr apps monitor them. arrId is the
// series, the movie or the artist.
export interface ShareKey extends TitleKey {
  seasonNumber?: number | null;
  episodeArrId?: number | null;
  albumArrId?: number | null;
}

export interface ShareableAlbum {
  albumArrId: number;
  title?: string;
  year: number;
  albumType?: string;
  isWhole: boolean;
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

// A title a friend offers, with however many of its files, for browsing by poster.
export interface PeerCatalogTitle {
  key: string;
  mediaKind: MediaKind;
  isAdult: boolean;
  isScene: boolean;
  title?: string;
  artist?: string;
  year: number;
  peerNames: string[];
  posterItemId: number;
  files: number;
  missing: number;
  upgrade: number;
  notInLibrary: number;
  have: number;
}

export interface PeerCatalogItem {
  id: number;
  peerId: number;
  peerName?: string;
  mediaKind: MediaKind;
  localStatus: PeerItemStatus;
  isAdult: boolean;
  isScene: boolean;
  title?: string;
  year: number;
  seasonNumber: number;
  seriesType?: string;
  episodeNumbers: number[];
  absoluteEpisodeNumbers: number[];
  episodeTitles?: string;
  episodeNames: string[];
  airDates: string[];
  sceneSeasonNumber?: number;
  sceneEpisodeNumbers: number[];
  artist?: string;
  trackCount: number;
  releaseTitle?: string;
  quality?: string;
  revision?: string;
  resolution: number;
  size: number;
  dateAdded?: string;
}

export interface PeerCatalogSeason {
  seasonNumber: number;
  status: PeerItemStatus;
  episodes: number;
  missing: number;
  upgrade: number;
  size: number;
  items: PeerCatalogItem[];
}

export interface PeerCatalogSeries {
  key: string;
  title?: string;
  year: number;
  seriesType?: string;
  posterItemId: number;
  status: PeerItemStatus;
  episodes: number;
  missing: number;
  upgrade: number;
  size: number;
  seasons: PeerCatalogSeason[];
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

export function albumShareId(title: TitleKey, albumArrId: number) {
  return `${titleId(title)}:a${albumArrId}`;
}

// One string for each thing a list can hold, so a list can be kept as a Set.
export function shareId(key: ShareKey) {
  if (key.albumArrId != null) {
    return albumShareId(key, key.albumArrId);
  }

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
  } else if (part?.startsWith('a')) {
    key.albumArrId = Number(part.slice(1));
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

export const useShareableAlbums = (title: TitleKey, enabled: boolean) =>
  useApiQuery<ShareableAlbum[]>({
    path: `${TITLES_PATH}/${title.applicationId}/${title.arrId}/albums`,
    queryOptions: { enabled },
  });

export const useHashingStatus = () =>
  useApiQuery<HashingStatus>({
    path: '/sharing/hashing',
    queryOptions: { refetchInterval: 5000 },
  });

export const usePeerCatalogSummaries = (refetchInterval?: number) =>
  useApiQuery<PeerCatalogSummary[]>({
    path: '/catalog/peers',
    queryOptions: { refetchInterval },
  });

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

// The apps whose libraries Nodearr reads. Prowlarr only searches, and holds no
// library.
export const useLibraryApplications = () => {
  const { data = [] } = useApiQuery<Application[]>({ path: '/applications' });

  return data.filter((app) => app.implementation !== 'Prowlarr');
};

// What a friend is offered, in a few words: everything, everything but some
// apps, or only their lists.
export function describeSharing(peer: Peer, applications: Application[]) {
  if (!peer.shareAll) {
    return translate('SharingListsOnly');
  }

  const leftOut = applications.filter((app) =>
    peer.excludedApplicationIds?.includes(app.id)
  );

  if (leftOut.length === 0) {
    return translate('SharingEverything');
  }

  if (leftOut.length === applications.length) {
    return translate('SharingListsOnly');
  }

  return translate('SharingEverythingBut', {
    apps: leftOut.map((app) => app.name).join(', '),
  });
}

export const useSetShareAll = () => {
  const queryClient = useQueryClient();

  return useMutation<
    unknown,
    ApiError,
    { id: number; shareAll: boolean; excludedApplicationIds?: number[] }
  >({
    mutationFn: ({ id, shareAll, excludedApplicationIds }) =>
      send('PUT', `${PEERS_PATH}/${id}/shareall`, {
        shareAll,
        excludedApplicationIds,
      }),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [PEERS_PATH] });
    },
  });
};

export type SendStatus =
  | 'waitingForInvite'
  | 'inviteLapsed'
  | 'notTold'
  | 'told';

export interface SentTitle {
  id: number;
  peerId?: number;
  peerName?: string;
  inviteId?: number;
  inviteExpires?: string;
  status: SendStatus;
  title?: string;
  summary?: string;
  mediaKind: MediaKind;
  items: ShareKey[];
  created: string;
  deliveredAt?: string;
  lastError?: string;
  inviteCode?: string;
}

export interface ReceivedTitle {
  id: number;
  peerId: number;
  peerName?: string;
  title?: string;
  summary?: string;
  mediaKind: MediaKind;
  isWhole: boolean;
  offerIds: string[];
  received: string;
  year: number;
  getItState: GetItState;
  getItMessage?: string;
  // An item of this node's copy of the friend's catalog for its poster, once it has one.
  posterItemId?: number | null;
  detailsAvailable: boolean;
}

export type GetItState = 'none' | 'working' | 'done' | 'failed';

export interface GetItOptions {
  applicationId: number;
  rootFolderPath?: string;
  qualityProfileId: number;
  metadataProfileId?: number | null;
  seriesType?: string | null;
  seasonFolder: boolean;
}

export interface GetItProfile {
  id: number;
  name?: string;
}

export interface GetItTarget {
  applicationId: number;
  applicationName?: string;
  implementation?: string;
  inLibrary: boolean;
  rootFolders: { path?: string; freeSpace?: number }[];
  qualityProfiles: GetItProfile[];
  metadataProfiles: GetItProfile[];
  defaults?: GetItOptions;
  problem?: string;
}

export interface GetItPlan {
  targets: GetItTarget[];
  problem?: string;
}

const SENDS_PATH = '/sharing/sends';
const RECEIVED_PATH = '/sharing/sends/received';

export const useSends = () => useApiQuery<SentTitle[]>({ path: SENDS_PATH });

export const useReceived = () =>
  useApiQuery<ReceivedTitle[]>({
    path: RECEIVED_PATH,
    queryOptions: {
      refetchInterval: (query) => (query.state.data?.length ? 5000 : false),
    },
  });

// To a friend, or, without one, to someone new with an invite of its own.
export const useSendTitle = () => {
  const queryClient = useQueryClient();

  return useMutation<
    SentTitle,
    ApiError,
    {
      peerId?: number;
      items: ShareKey[];
      validHours?: number;
      shareAll?: boolean;
    }
  >({
    mutationFn: (body) => send<SentTitle>('POST', SENDS_PATH, body),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [SENDS_PATH] });
      queryClient.invalidateQueries({ queryKey: [LISTS_PATH] });
      queryClient.invalidateQueries({ queryKey: ['/nodes/invites'] });
    },
  });
};

export const useForgetSend = () => {
  const queryClient = useQueryClient();

  return useMutation<unknown, ApiError, number>({
    mutationFn: (id) => send('DELETE', `${SENDS_PATH}/${id}`),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [SENDS_PATH] });
      queryClient.invalidateQueries({ queryKey: ['/nodes/invites'] });
    },
  });
};

export const useDismissReceived = () => {
  const queryClient = useQueryClient();

  return useMutation<unknown, ApiError, number>({
    mutationFn: (id) => send('POST', `${RECEIVED_PATH}/${id}/dismiss`),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [RECEIVED_PATH] });
    },
  });
};

// Asked when a card is shown: the Arr apps' root folders and profiles, with the defaults.
export const useGetItPlan = (id: number, enabled: boolean) =>
  useApiQuery<GetItPlan>({
    path: `${RECEIVED_PATH}/${id}/getit`,
    queryOptions: { enabled },
  });

export const useGetIt = () => {
  const queryClient = useQueryClient();

  return useMutation<unknown, ApiError, { id: number; options: GetItOptions }>({
    mutationFn: ({ id, options }) =>
      send('POST', `${RECEIVED_PATH}/${id}/getit`, options),
    onSettled: (_data, _error, { id }) => {
      queryClient.invalidateQueries({ queryKey: [RECEIVED_PATH] });
      queryClient.invalidateQueries({
        queryKey: [`${RECEIVED_PATH}/${id}/getit`],
      });
    },
  });
};
