export interface LocalCatalogSummary {
  applicationId: number;
  applicationName: string;
  implementation: string;
  series: number;
  episodes: number;
  episodeFiles: number;
  movies: number;
  movieFiles: number;
  artists: number;
  albums: number;
  trackFiles: number;
  missingFiles: number;
  lastSyncStarted?: string;
  lastSyncCompleted?: string;
  lastSyncError?: string;
}

export type LocalCatalogMediaType = 'episode' | 'movie' | 'track';
export type CatalogKind = 'series' | 'movies' | 'music' | 'scenes';

export type PosterSubject = 'series' | 'movie' | 'album' | 'artist';

// A series or movie in the local catalog, for the Posters view.
export interface LocalCatalogTitle {
  applicationId: number;
  applicationName?: string;
  mediaType: 'series' | 'movie' | 'album' | 'artist';
  kind: CatalogKind;
  isAdult: boolean;
  posterSubject: PosterSubject;
  posterArrId: number;
  title?: string;
  year: number;
  files: number;
  artist?: string;
  artistArrId: number;
}

export interface LocalCatalogFile {
  id: number;
  mediaType: LocalCatalogMediaType;
  isAdult: boolean;
  isScene: boolean;
  applicationId: number;
  applicationName?: string;
  title?: string;
  year: number;
  posterSubject: PosterSubject;
  posterArrId: number;
  tvdbId: number;
  tmdbId: number;
  imdbId?: string;
  artistArrId: number;
  seasonNumber?: number;
  episodeNumbers: number[];
  episodeArrIds: number[];
  episodeTitles: string[];
  absoluteEpisodeNumbers: number[];
  sceneSeasonNumber?: number;
  sceneEpisodeNumbers: number[];
  airDates: string[];
  artist?: string;
  albumTitle?: string;
  trackTitle?: string;
  trackNumber: number;
  discNumber: number;
  isMissing: boolean;
  seriesType?: string;
  path?: string;
  relativePath?: string;
  localPath?: string;
  access: 'readable' | 'missing' | 'notAllowed';
  readable: boolean;
  size: number;
  quality?: string;
  revision?: string;
  resolution: number;
  languages?: string;
  customFormats?: string;
  releaseGroup?: string;
  sceneName?: string;
  videoCodec?: string;
  audioCodec?: string;
  dateAdded?: string;
  missingSince?: string;
}

export interface LocalCatalogSeason {
  seasonNumber: number;
  episodes: number;
  missing: number;
  size: number;
  files: LocalCatalogFile[];
}

export interface LocalCatalogSeries {
  applicationId: number;
  applicationName?: string;
  title?: string;
  year: number;
  seriesType?: string;
  posterArrId: number;
  episodes: number;
  missing: number;
  size: number;
  seasons: LocalCatalogSeason[];
}
