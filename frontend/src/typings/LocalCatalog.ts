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

export type LocalCatalogMediaType = 'episode' | 'movie';

export interface LocalCatalogFile {
  id: number;
  mediaType: LocalCatalogMediaType;
  applicationId: number;
  applicationName?: string;
  title?: string;
  year: number;
  tvdbId: number;
  tmdbId: number;
  imdbId?: string;
  seasonNumber?: number;
  episodeNumbers: number[];
  episodeTitles: string[];
  path?: string;
  relativePath?: string;
  localPath?: string;
  readable: boolean;
  size: number;
  quality?: string;
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
