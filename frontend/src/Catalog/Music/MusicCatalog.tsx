import React, { useCallback, useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import TableRow from 'Components/Table/TableRow';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import CatalogAvailability from '../CatalogAvailability';
import CatalogDetailsButton from '../Details/CatalogDetailsButton';
import CatalogGetItButton from '../GetIt/CatalogGetItButton';
import { CatalogGetItSelection } from '../GetIt/useCatalogGetIt';
import Poster from '../Poster';
import SendFromCatalogButton from '../Sharing/SendFromCatalogButton';
import { PeerItemStatus, ShareKey } from '../useSharing';
import styles from '../Catalog.css';

export interface MusicEntry {
  key: string;
  title?: string;
  parentTitle?: string;
  sourceName?: string;
  path: string;
  posterPath?: string;
  year: number;
  albumCount: number;
  trackCount: number;
  localStatus?: PeerItemStatus | null;
  send?: ShareKey;
  getIt?: CatalogGetItSelection;
  wasSent?: boolean;
}

const columns = [
  { name: 'poster', label: '', isVisible: true },
  {
    name: 'source',
    label: () => translate('CatalogDetailsSource'),
    isVisible: true,
  },
  { name: 'title', label: () => translate('Title'), isVisible: true },
  {
    name: 'availability',
    label: () => translate('CatalogDetailsLocalStatus'),
    isVisible: true,
  },
  { name: 'actions', label: '', isVisible: true },
];

function MusicActions({
  entry,
  overlay = false,
}: {
  entry: MusicEntry;
  overlay?: boolean;
}) {
  return (
    <>
      {entry.send ? (
        <SendFromCatalogButton
          className={overlay ? styles.tileSend : undefined}
          titleKey={entry.send}
          items={[entry.send]}
          label={entry.title ?? ''}
          showLabel={!overlay}
        />
      ) : null}
      {entry.getIt ? (
        <CatalogGetItButton
          {...entry.getIt}
          mediaKind="music"
          title={entry.title ?? ''}
          variant={overlay ? 'icon' : 'button'}
          className={overlay ? styles.tileSend : undefined}
        />
      ) : null}
    </>
  );
}

function MusicCatalog({
  peer,
  view,
  status,
}: {
  peer: boolean;
  view: 'posters' | 'table';
  status?: PeerItemStatus;
}) {
  const storageKey = `nodearr.${peer ? 'peer' : 'local'}Catalog.musicLevel`;
  const [level, setLevel] = useState(() => {
    try {
      return localStorage.getItem(storageKey) === 'album' ? 'album' : 'artist';
    } catch {
      return 'artist';
    }
  });
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [level, status, view]);
  const query = usePagedApiQuery<MusicEntry>({
    path: `/catalog/music/${peer ? 'peer' : 'local'}`,
    page,
    pageSize: 48,
    sortKey: 'title',
    sortDirection: 'ascending',
    queryParams: { level, ...(peer && status ? { status } : {}) },
    queryOptions: { refetchInterval: 5000 },
  });
  const chooseLevel = useCallback(
    (next: string) => {
      setLevel(next);
      setPage(1);
      try {
        localStorage.setItem(storageKey, next);
      } catch {
        /* This visit only. */
      }
    },
    [storageKey]
  );
  const artists = useCallback(() => chooseLevel('artist'), [chooseLevel]);
  const albums = useCallback(() => chooseLevel('album'), [chooseLevel]);
  const first = useCallback(() => setPage(1), []);
  const previous = useCallback(
    () => setPage((current) => Math.max(1, current - 1)),
    []
  );
  const next = useCallback(
    () => setPage((current) => Math.min(query.totalPages, current + 1)),
    [query.totalPages]
  );
  const last = useCallback(() => setPage(query.totalPages), [query.totalPages]);
  const image = (entry: MusicEntry, size: 'tile' | 'thumb') => (
    <CatalogDetailsButton
      path={entry.path}
      label={entry.title ?? ''}
      variant="poster"
    >
      <Poster
        src={
          entry.posterPath
            ? `${window.Nodearr.urlBase}/api/v5${entry.posterPath}`
            : null
        }
        title={entry.title ?? ''}
        isSquare={true}
        size={size}
      />
    </CatalogDetailsButton>
  );
  const title = (entry: MusicEntry) => (
    <>
      <CatalogDetailsButton
        path={entry.path}
        label={entry.title ?? ''}
        variant="title"
      >
        {entry.title}
        {entry.year ? ` (${entry.year})` : ''}
      </CatalogDetailsButton>
      {entry.parentTitle ? (
        <div className={styles.muted}>{entry.parentTitle}</div>
      ) : null}
      <div className={styles.muted}>
        {translate(level === 'artist' ? 'MusicAlbumCount' : 'MusicTrackCount', {
          count: level === 'artist' ? entry.albumCount : entry.trackCount,
        })}
      </div>
    </>
  );
  return (
    <section aria-label={translate('Music')}>
      <div className={styles.buttons}>
        <Button isDisabled={level === 'artist'} onPress={artists}>
          {translate('Artists')}
        </Button>
        <Button isDisabled={level === 'album'} onPress={albums}>
          {translate('Albums')}
        </Button>
      </div>
      {query.isLoading ? <LoadingIndicator /> : null}
      {query.error ? (
        <Alert kind={kinds.DANGER}>
          {translate('CatalogDetailsUnavailable')}
        </Alert>
      ) : null}
      {!query.isLoading && !query.error && query.records.length === 0 ? (
        <Alert kind={kinds.INFO}>{translate('MusicNothingHere')}</Alert>
      ) : null}
      {view === 'posters' ? (
        <div className={styles.tiles}>
          {query.records.map((entry) => (
            <div key={entry.key} className={styles.tile}>
              <div className={styles.tilePoster}>
                {image(entry, 'tile')}
                <MusicActions entry={entry} overlay={true} />
              </div>
              <div className={styles.tileTitle}>{title(entry)}</div>
              {entry.localStatus ? (
                <CatalogAvailability status={entry.localStatus} />
              ) : null}
              <div className={styles.tileDetails}>{entry.sourceName}</div>
            </div>
          ))}
        </div>
      ) : (
        <Table columns={columns} canModifyColumns={false}>
          <TableBody>
            {query.records.map((entry) => (
              <TableRow key={entry.key}>
                <TableRowCell className={styles.posterCell}>
                  {image(entry, 'thumb')}
                </TableRowCell>
                <TableRowCell>{entry.sourceName}</TableRowCell>
                <TableRowCell>{title(entry)}</TableRowCell>
                <TableRowCell>
                  {entry.localStatus ? (
                    <CatalogAvailability status={entry.localStatus} />
                  ) : null}
                </TableRowCell>
                <TableRowCell>
                  <MusicActions entry={entry} />
                </TableRowCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      <TablePager
        page={page}
        totalPages={query.totalPages}
        totalRecords={query.totalRecords}
        isFetching={query.isFetching}
        onFirstPagePress={first}
        onPreviousPagePress={previous}
        onNextPagePress={next}
        onLastPagePress={last}
        onPageSelect={setPage}
      />
    </section>
  );
}

export default MusicCatalog;
