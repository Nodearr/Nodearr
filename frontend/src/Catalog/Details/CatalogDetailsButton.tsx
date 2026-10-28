import React, { useCallback, useRef, useState } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { OnScroll } from 'Components/Scroller/Scroller';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import CatalogAvailability from '../CatalogAvailability';
import CatalogGetItButton from '../GetIt/CatalogGetItButton';
import { CatalogGetItSelection } from '../GetIt/useCatalogGetIt';
import { MusicEntry } from '../Music/MusicCatalog';
import Poster from '../Poster';
import SendFromCatalogButton from '../Sharing/SendFromCatalogButton';
import useCatalogMissingEpisodes from '../useCatalogMissingEpisodes';
import { MediaKind, PeerItemStatus, ShareKey } from '../useSharing';
import catalogStyles from '../Catalog.css';
import styles from './CatalogDetails.css';

interface CatalogDetails {
  kind: 'series' | 'episode' | 'movie' | 'film' | 'scene' | 'artist' | 'album';
  parentPath?: string;
  foreignArtistId?: string;
  foreignAlbumId?: string;
  albumType?: string;
  albums: MusicEntry[];
  tracks: {
    title?: string;
    number: number;
    disc: number;
    duration: number;
    hasFile: boolean;
    quality?: string;
    size: number;
    localStatus?: PeerItemStatus | null;
  }[];
  title?: string;
  parentTitle?: string;
  sourceName?: string;
  sentSummary?: string;
  posterPath?: string;
  year: number;
  overview?: string;
  genres?: string;
  network?: string;
  runtime: number;
  seasonNumber: number;
  episodeNumber: number;
  absoluteEpisodeNumber?: number;
  airDate?: string;
  studio?: string;
  releaseDate?: string;
  code?: string;
  tvdbId: number;
  tmdbId: number;
  imdbId?: string;
  quality?: string;
  resolution: number;
  resolutions: number[];
  episodes: {
    seasonNumber: number;
    episodeNumber: number;
    title?: string;
    path: string;
    hasFile: boolean;
    wasSent?: boolean;
    localStatus?: PeerItemStatus | null;
  }[];
  send?: ShareKey;
  getIt?: CatalogGetItSelection & { mediaKind: MediaKind };
  localStatus?: PeerItemStatus | null;
  hasFile?: boolean | null;
  size: number;
}

function hasLocalFile(episode: CatalogDetails['episodes'][number]) {
  return episode.localStatus == null
    ? episode.hasFile
    : episode.localStatus === 'have' || episode.localStatus === 'upgrade';
}

interface CatalogDetailsButtonProps {
  path: string;
  label: string;
  variant: 'poster' | 'title' | 'episode';
  children: React.ReactNode;
  options?: { path: string; label: string }[];
}

function Detail({ label, value }: { label: string; value?: React.ReactNode }) {
  return value ? (
    <div className={styles.detail}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  ) : null;
}

function EpisodeOption({
  option,
  selectedPath,
  onSelect,
}: {
  option: { path: string; label: string };
  selectedPath: string;
  onSelect: (path: string) => void;
}) {
  const handleClick = useCallback(
    () => onSelect(option.path),
    [onSelect, option.path]
  );

  return (
    <button
      type="button"
      className={selectedPath === option.path ? styles.selected : undefined}
      aria-pressed={selectedPath === option.path}
      onClick={handleClick}
    >
      {option.label}
    </button>
  );
}

function SeriesEpisode({
  episode,
  onSelect,
  showAvailability,
}: {
  episode: CatalogDetails['episodes'][number];
  onSelect: (path: string) => void;
  showAvailability: boolean;
}) {
  const select = useCallback(
    () => onSelect(episode.path),
    [onSelect, episode.path]
  );
  return (
    <li>
      <button type="button" onClick={select}>
        {`S${String(episode.seasonNumber).padStart(2, '0')}E${String(
          episode.episodeNumber
        ).padStart(2, '0')}`}
        {episode.title ? ` · ${episode.title}` : ''}
      </button>
      {episode.wasSent ? (
        <span className={styles.sent}>{translate('ReceivedEpisodeSent')}</span>
      ) : null}
      {showAvailability ? (
        <CatalogAvailability
          status={episode.localStatus ?? (episode.hasFile ? 'have' : 'missing')}
        />
      ) : null}
    </li>
  );
}

function AlbumOption({
  album,
  onSelect,
}: {
  album: MusicEntry;
  onSelect: (path: string) => void;
}) {
  const select = useCallback(
    () => onSelect(album.path),
    [album.path, onSelect]
  );
  const title = album.title ?? '';
  const label = `${translate('Details')}: ${title}`;

  // As a poster tile in either catalog: the poster opens it, Send or Get it is an icon over the
  // poster, and the title and year are underneath.
  return (
    <div className={catalogStyles.tile}>
      <div className={catalogStyles.tilePoster}>
        <button
          type="button"
          className={styles.albumLink}
          aria-label={label}
          onClick={select}
        >
          <Poster
            src={
              album.posterPath
                ? `${window.Nodearr.urlBase}/api/v5${album.posterPath}`
                : null
            }
            title={title}
            size="tile"
            isSquare={true}
          />
        </button>
        {album.send ? (
          <SendFromCatalogButton
            className={catalogStyles.tileSend}
            titleKey={album.send}
            items={[album.send]}
            label={title}
          />
        ) : null}
        {album.getIt ? (
          <CatalogGetItButton
            {...album.getIt}
            mediaKind="music"
            title={title}
            variant="icon"
            className={catalogStyles.tileSend}
          />
        ) : null}
      </div>
      <div className={catalogStyles.tileTitle} title={title}>
        <button type="button" className={styles.title} onClick={select}>
          {title}
        </button>
      </div>
      {album.year ? (
        <div className={catalogStyles.tileDetails}>{album.year}</div>
      ) : null}
      {album.localStatus ? (
        <CatalogAvailability status={album.localStatus} />
      ) : null}
      {album.wasSent ? (
        <div className={catalogStyles.tileDetails}>
          {translate('ReceivedAlbumSent')}
        </div>
      ) : null}
    </div>
  );
}

function kindLabel(kind: CatalogDetails['kind']) {
  switch (kind) {
    case 'series':
      return translate('Series');
    case 'artist':
      return translate('Artist');
    case 'album':
      return translate('Album');
    case 'episode':
      return translate('Episode');
    case 'scene':
      return translate('Scene');
    case 'film':
      return translate('CatalogDetailsFilm');
    default:
      return translate('CatalogDetailsMovie');
  }
}

function CatalogDetailsModal({
  path,
  options,
  onClose,
}: {
  path: string;
  options?: { path: string; label: string }[];
  onClose: () => void;
}) {
  const [selectedPath, setSelectedPath] = useState(path);
  const [history, setHistory] = useState<string[]>([]);
  const positions = useRef<Record<string, number>>({});
  const peer =
    !path.startsWith('/catalog/local/') &&
    !path.startsWith('/catalog/music/local/');
  const select = useCallback(
    (next: string) => {
      setHistory((current) => [...current, selectedPath]);
      setSelectedPath(next);
    },
    [selectedPath]
  );
  const [showMissing, setShowMissing] = useCatalogMissingEpisodes(peer, true);
  const handleShowMissingChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setShowMissing(event.target.checked);
    },
    [setShowMissing]
  );
  const backToSeries = useCallback(() => {
    setSelectedPath(history[history.length - 1] ?? path);
    setHistory((current) => current.slice(0, -1));
  }, [history, path]);
  const { data, isLoading, error } = useApiQuery<CatalogDetails>({
    path: selectedPath,
    queryOptions: {
      refetchInterval: peer ? 5000 : false,
    },
  });
  const scroll = useCallback(
    ({ scrollTop }: OnScroll) => {
      if (!isLoading) positions.current[selectedPath] = scrollTop;
    },
    [isLoading, selectedPath]
  );
  const parent = useCallback(() => {
    if (data?.parentPath) select(data.parentPath);
  }, [data?.parentPath, select]);
  const episodeLabel =
    data?.kind === 'episode'
      ? `${data.parentTitle ?? ''} · S${String(data.seasonNumber).padStart(
          2,
          '0'
        )}E${String(data.episodeNumber).padStart(2, '0')}`
      : undefined;
  const episodes = (data?.episodes ?? []).filter(
    (episode) =>
      showMissing ||
      episode.wasSent ||
      (peer ? episode.localStatus !== 'missing' : hasLocalFile(episode))
  );
  const seasons = Array.from(
    new Set(episodes.map((episode) => episode.seasonNumber))
  );

  let availability = data?.localStatus;
  if (availability == null && data?.hasFile != null) {
    availability = data.hasFile ? 'have' : 'missing';
  }

  return (
    <Modal isOpen={true} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>
          {data?.title || episodeLabel || translate('Details')}
        </ModalHeader>
        <ModalBody
          key={selectedPath}
          scrollTop={
            isLoading ? undefined : positions.current[selectedPath] ?? 0
          }
          onScroll={scroll}
        >
          {history.length > 0 && !options ? (
            <button
              type="button"
              className={styles.back}
              onClick={backToSeries}
            >
              {translate(
                data?.kind === 'artist' || data?.kind === 'album'
                  ? 'Back'
                  : 'CatalogDetailsBackToSeries'
              )}
            </button>
          ) : null}
          {options && options.length > 1 ? (
            <div className={styles.choices}>
              {options.map((option) => (
                <EpisodeOption
                  key={option.path}
                  option={option}
                  selectedPath={selectedPath}
                  onSelect={select}
                />
              ))}
            </div>
          ) : null}
          {isLoading ? <LoadingIndicator /> : null}
          {error ? (
            <Alert kind={kinds.DANGER}>
              {translate('CatalogDetailsUnavailable')}
            </Alert>
          ) : null}
          {data && !error ? (
            <div className={styles.card}>
              {data.posterPath ? (
                <div className={styles.cardPoster}>
                  <Poster
                    src={`${window.Nodearr.urlBase}/api/v5${data.posterPath}`}
                    title={data.title ?? data.parentTitle ?? ''}
                    size="tile"
                    isSquare={data.kind === 'artist' || data.kind === 'album'}
                  />
                </div>
              ) : null}
              <div className={styles.cardContent}>
                <div className={styles.kind}>{kindLabel(data.kind)}</div>
                {data.send ? (
                  <div className={styles.send}>
                    <SendFromCatalogButton
                      titleKey={data.send}
                      items={[data.send]}
                      label={data.title ?? data.parentTitle ?? ''}
                      showLabel={true}
                    />
                  </div>
                ) : null}
                {data.getIt && !error ? (
                  <div className={styles.send}>
                    <CatalogGetItButton
                      key={selectedPath}
                      itemId={data.getIt.itemId}
                      scope={data.getIt.scope}
                      mediaKind={data.getIt.mediaKind}
                      title={episodeLabel ?? data.title ?? ''}
                      variant="card"
                    />
                  </div>
                ) : null}
                {episodeLabel ? (
                  <div className={styles.parent}>{episodeLabel}</div>
                ) : null}
                {data.parentPath ? (
                  <button
                    type="button"
                    className={styles.back}
                    onClick={parent}
                  >
                    {data.parentTitle}
                  </button>
                ) : null}
                {data.sentSummary ? (
                  <p>
                    {translate('ReceivedDetailsSelection', {
                      selection: data.sentSummary,
                    })}
                    {data.episodes.some((episode) => episode.wasSent)
                      ? ` ${translate('ReceivedDetailsEpisodes')}`
                      : null}
                  </p>
                ) : null}
                {data.overview ? (
                  <p className={styles.overview}>{data.overview}</p>
                ) : null}
                <dl className={styles.details}>
                  <Detail
                    label={translate('CatalogDetailsSource')}
                    value={data.sourceName}
                  />
                  <Detail
                    label={translate('CatalogDetailsLocalStatus')}
                    value={
                      availability == null ? undefined : (
                        <CatalogAvailability status={availability} />
                      )
                    }
                  />
                  <Detail
                    label={translate('Year')}
                    value={data.year || undefined}
                  />
                  <Detail label={translate('Genres')} value={data.genres} />
                  <Detail label={translate('Network')} value={data.network} />
                  <Detail
                    label={translate('Runtime')}
                    value={data.runtime > 0 ? `${data.runtime} min` : undefined}
                  />
                  <Detail label={translate('AirDate')} value={data.airDate} />
                  <Detail
                    label={translate('CatalogDetailsStudio')}
                    value={data.studio}
                  />
                  <Detail
                    label={translate('CatalogDetailsReleaseDate')}
                    value={data.releaseDate}
                  />
                  <Detail
                    label={translate('CatalogDetailsCode')}
                    value={data.code}
                  />
                  <Detail label={translate('Quality')} value={data.quality} />
                  <Detail
                    label={translate('CatalogDetailsResolution')}
                    value={
                      data.kind === 'series'
                        ? data.resolutions
                            .map((resolution) => `${resolution}p`)
                            .join(', ')
                        : data.resolution > 0 && `${data.resolution}p`
                    }
                  />
                  <Detail
                    label={translate('Size')}
                    value={data.size > 0 ? formatBytes(data.size) : undefined}
                  />
                  <Detail label="TVDB" value={data.tvdbId || undefined} />
                  <Detail label="TMDb" value={data.tmdbId || undefined} />
                  <Detail label="IMDb" value={data.imdbId} />
                  <Detail
                    label={translate('MusicAlbumType')}
                    value={data.albumType}
                  />
                  <Detail
                    label={translate('MusicBrainzArtist')}
                    value={data.foreignArtistId}
                  />
                  <Detail
                    label={translate('MusicBrainzAlbum')}
                    value={data.foreignAlbumId}
                  />
                </dl>
                {data.kind === 'artist' ? (
                  <section aria-label={translate('Albums')}>
                    <h3>{translate('Albums')}</h3>
                    <div className={styles.albums}>
                      {data.albums.map((album) => (
                        <AlbumOption
                          key={album.key}
                          album={album}
                          onSelect={select}
                        />
                      ))}
                    </div>
                    {data.albums.length === 0 ? (
                      <p>{translate('MusicNothingHere')}</p>
                    ) : null}
                  </section>
                ) : null}
                {data.kind === 'album' ? (
                  <section
                    className={styles.episodes}
                    aria-label={translate('Tracks')}
                  >
                    <h3>{translate('Tracks')}</h3>
                    <p>
                      {translate(peer ? 'MusicPeerTracks' : 'MusicLocalTracks')}
                    </p>
                    <ol className={styles.tracks}>
                      {data.tracks.map((track, index) => (
                        <li key={`${track.disc}-${track.number}-${index}`}>
                          <span>
                            {track.disc > 1 ? `${track.disc}-` : ''}
                            {track.number || index + 1}.{' '}
                            {track.title || translate('MusicUnknownTrack')}
                          </span>
                          {track.duration > 0 ? (
                            <span>{`${Math.floor(
                              track.duration / 60000
                            )}:${String(
                              Math.floor(track.duration / 1000) % 60
                            ).padStart(2, '0')}`}</span>
                          ) : null}
                          {track.quality ? <span>{track.quality}</span> : null}
                          {peer && track.localStatus == null ? (
                            <span>{translate('MusicTrackStatusUnknown')}</span>
                          ) : (
                            <CatalogAvailability
                              status={
                                track.localStatus ??
                                (track.hasFile ? 'have' : 'missing')
                              }
                            />
                          )}
                        </li>
                      ))}
                    </ol>
                    {data.tracks.length === 0 ? (
                      <p>{translate('MusicTrackListUnavailable')}</p>
                    ) : null}
                  </section>
                ) : null}
                {data.kind === 'series' && data.episodes.length > 0 ? (
                  <section className={styles.episodes}>
                    <h3>{translate('Episodes')}</h3>
                    {data.send ||
                    data.episodes.some(
                      (episode) => episode.localStatus != null
                    ) ? (
                      <label className={styles.missingFilter}>
                        <input
                          type="checkbox"
                          checked={showMissing}
                          onChange={handleShowMissingChange}
                        />
                        {translate('CatalogShowMissingEpisodes')}
                      </label>
                    ) : null}
                    {seasons.length === 0 ? (
                      <p>{translate('CatalogNoDownloadedEpisodes')}</p>
                    ) : null}
                    {seasons.map((season, index) => (
                      <details
                        key={season}
                        open={index === 0 ? true : undefined}
                      >
                        <summary>
                          {season === 0
                            ? translate('Specials')
                            : `${translate('Season')} ${season}`}
                        </summary>
                        <ul>
                          {episodes
                            .filter(
                              (episode) => episode.seasonNumber === season
                            )
                            .map((episode) => (
                              <SeriesEpisode
                                key={episode.path}
                                episode={episode}
                                showAvailability={
                                  Boolean(data.send) ||
                                  episode.localStatus != null
                                }
                                onSelect={select}
                              />
                            ))}
                        </ul>
                      </details>
                    ))}
                  </section>
                ) : null}
              </div>
            </div>
          ) : null}
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}

function CatalogDetailsButton({
  path,
  label,
  variant,
  children,
  options,
}: CatalogDetailsButtonProps) {
  const [isOpen, setIsOpen] = useState(false);
  const open = useCallback(() => setIsOpen(true), []);
  const close = useCallback(() => setIsOpen(false), []);

  return (
    <>
      <button
        type="button"
        className={styles[variant]}
        aria-label={`${translate('Details')}: ${label}`}
        onClick={open}
      >
        {children}
      </button>
      {isOpen ? (
        <CatalogDetailsModal path={path} options={options} onClose={close} />
      ) : null}
    </>
  );
}

export default CatalogDetailsButton;
