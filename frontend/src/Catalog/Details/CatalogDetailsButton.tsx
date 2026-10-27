import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import Poster from '../Poster';
import SendFromCatalogButton from '../Sharing/SendFromCatalogButton';
import { PeerItemStatus, ShareKey } from '../useSharing';
import styles from './CatalogDetails.css';

interface CatalogDetails {
  kind: 'series' | 'episode' | 'movie' | 'film' | 'scene';
  title?: string;
  parentTitle?: string;
  sourceName?: string;
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
    localStatus?: PeerItemStatus | null;
  }[];
  send?: ShareKey;
  localStatus?: PeerItemStatus | null;
  size: number;
}

function statusLabel(status: PeerItemStatus) {
  const labels: Record<PeerItemStatus, string> = {
    have: 'PeerItemHave',
    upgrade: 'PeerItemUpgrade',
    missing: 'PeerItemMissing',
    notInLibrary: 'PeerItemNotInLibrary',
  };
  return translate(labels[status]);
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
      {showAvailability ? (
        <span
          className={hasLocalFile(episode) ? styles.available : styles.missing}
        >
          {episode.localStatus == null
            ? translate(episode.hasFile ? 'CatalogDetailsInLibrary' : 'Missing')
            : statusLabel(episode.localStatus)}
        </span>
      ) : null}
    </li>
  );
}

function kindLabel(kind: CatalogDetails['kind']) {
  switch (kind) {
    case 'series':
      return translate('Series');
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
  const [showMissing, setShowMissing] = useState(true);
  const handleShowMissingChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setShowMissing(event.target.checked);
    },
    []
  );
  const backToSeries = useCallback(() => setSelectedPath(path), [path]);
  const { data, isLoading, error } = useApiQuery<CatalogDetails>({
    path: selectedPath,
  });
  const episodeLabel =
    data?.kind === 'episode'
      ? `${data.parentTitle ?? ''} · S${String(data.seasonNumber).padStart(
          2,
          '0'
        )}E${String(data.episodeNumber).padStart(2, '0')}`
      : undefined;
  const episodes = (data?.episodes ?? []).filter(
    (episode) => showMissing || hasLocalFile(episode)
  );
  const seasons = Array.from(
    new Set(episodes.map((episode) => episode.seasonNumber))
  );

  return (
    <Modal isOpen={true} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>
          {data?.title || episodeLabel || translate('Details')}
        </ModalHeader>
        <ModalBody>
          {selectedPath !== path && !options ? (
            <button
              type="button"
              className={styles.back}
              onClick={backToSeries}
            >
              {translate('CatalogDetailsBackToSeries')}
            </button>
          ) : null}
          {options && options.length > 1 ? (
            <div className={styles.choices}>
              {options.map((option) => (
                <EpisodeOption
                  key={option.path}
                  option={option}
                  selectedPath={selectedPath}
                  onSelect={setSelectedPath}
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
          {data ? (
            <div className={styles.card}>
              {data.posterPath ? (
                <div className={styles.cardPoster}>
                  <Poster
                    src={`${window.Nodearr.urlBase}/api/v5${data.posterPath}`}
                    title={data.title ?? data.parentTitle ?? ''}
                    size="tile"
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
                {episodeLabel ? (
                  <div className={styles.parent}>{episodeLabel}</div>
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
                      data.localStatus == null
                        ? undefined
                        : statusLabel(data.localStatus)
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
                </dl>
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
                                onSelect={setSelectedPath}
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
