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
  size: number;
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

  return (
    <Modal isOpen={true} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>
          {data?.title || episodeLabel || translate('Details')}
        </ModalHeader>
        <ModalBody>
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
