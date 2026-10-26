import React, { useCallback, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import TableRow from 'Components/Table/TableRow';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import { CatalogKind, LocalCatalogSummary } from 'typings/LocalCatalog';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatBytes from 'Utilities/Number/formatBytes';
import titleWithYear from 'Utilities/String/titleWithYear';
import translate from 'Utilities/String/translate';
import CatalogKindButton from '../CatalogKindButton';
import { getAvailableCatalogKinds } from '../catalogKinds';
import CatalogDetailsButton from '../Details/CatalogDetailsButton';
import CatalogGetItButton from '../GetIt/CatalogGetItButton';
import Poster, { peerPosterSrc } from '../Poster';
import {
  PeerCatalogItem,
  PeerCatalogSeries,
  PeerCatalogTitle,
  PeerItemStatus,
  usePeerCatalogSummaries,
} from '../useSharing';
import styles from '../Catalog.css';

const PAGE_SIZE = 50;

// A grid of posters fills a page with fewer, larger items.
const POSTER_PAGE_SIZE = 48;

type View = 'table' | 'posters';

const VIEW_KEY = 'nodearr.peerCatalog.view';
const KIND_KEY = 'nodearr.peerCatalog.kind';

function readKind(): CatalogKind {
  try {
    const value = window.localStorage.getItem(KIND_KEY);
    return value === 'movies' || value === 'music' || value === 'scenes'
      ? value
      : 'series';
  } catch {
    return 'series';
  }
}

function readView(): View {
  try {
    return window.localStorage.getItem(VIEW_KEY) === 'posters'
      ? 'posters'
      : 'table';
  } catch {
    return 'table';
  }
}

const COLUMNS: Column[] = [
  { name: 'poster', label: '', isVisible: true },
  { name: 'peerName', label: () => translate('Peer'), isVisible: true },
  {
    name: 'title',
    label: () => translate('Title'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'localStatus',
    label: () => translate('Status'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'quality',
    label: () => translate('Quality'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'releaseTitle',
    label: () => translate('ReleaseTitle'),
    isVisible: true,
  },
  { name: 'getIt', label: '', isVisible: true },
];

const STATUSES: PeerItemStatus[] = [
  'missing',
  'upgrade',
  'notInLibrary',
  'have',
];

const STATUS_LABELS: Record<PeerItemStatus, string> = {
  missing: 'PeerItemMissing',
  upgrade: 'PeerItemUpgrade',
  notInLibrary: 'PeerItemNotInLibrary',
  have: 'PeerItemHave',
};

const STATUS_KINDS: Record<
  PeerItemStatus,
  'danger' | 'info' | 'default' | 'success'
> = {
  missing: 'danger',
  upgrade: 'info',
  notInLibrary: 'default',
  have: 'success',
};

function pad(value: number) {
  return value.toString().padStart(2, '0');
}

function describe(item: PeerCatalogItem) {
  if (item.mediaKind === 'music') {
    const album = titleWithYear(item.title, item.year);
    const tracks =
      item.trackCount === 1
        ? translate('SharingOneTrack')
        : translate('SharingTrackCount', { count: item.trackCount });

    return `${item.artist} - ${album}, ${tracks}`;
  }

  if (item.mediaKind === 'movie') {
    return titleWithYear(item.title, item.year);
  }

  const episodes =
    item.seriesType === 'anime' && item.absoluteEpisodeNumbers.length > 0
      ? item.absoluteEpisodeNumbers.map((n) => `#${pad(n)}`).join(' + ')
      : item.episodeNumbers.map((n) => `E${pad(n)}`).join('');
  const airDate = item.seriesType === 'daily' ? item.airDates[0] : undefined;
  const sceneNumbers =
    item.sceneSeasonNumber !== undefined &&
    (item.sceneSeasonNumber !== item.seasonNumber ||
      item.sceneEpisodeNumbers.some(
        (number, index) => number !== item.episodeNumbers[index]
      ))
      ? ` · Scene S${pad(item.sceneSeasonNumber)}${item.sceneEpisodeNumbers
          .map((number) => `E${pad(number)}`)
          .join('')}`
      : '';

  return `${item.title} - S${pad(item.seasonNumber)}${
    airDate ? ` ${airDate}` : ''
  }${episodes}${sceneNumbers}`;
}

function StatusButton({
  status,
  count,
  isSelected,
  onPress,
}: {
  status?: PeerItemStatus;
  count: number;
  isSelected: boolean;
  onPress: (status?: PeerItemStatus) => void;
}) {
  const handlePress = useCallback(() => {
    onPress(status);
  }, [status, onPress]);

  return (
    <Button
      kind={isSelected ? kinds.PRIMARY : kinds.DEFAULT}
      onPress={handlePress}
    >
      {status ? translate(STATUS_LABELS[status]) : translate('All')} ({count})
    </Button>
  );
}

// "Pingu (1990)", or "Slint - Spiderland (1991)" for an album.
function describeTitle(title: PeerCatalogTitle) {
  const name = title.artist ? `${title.artist} - ${title.title}` : title.title;

  return titleWithYear(name, title.year);
}

function peerTitleDetailsPath(title: PeerCatalogTitle) {
  if (title.mediaKind === 'music') {
    return undefined;
  }

  let kind = 'movie';
  if (title.mediaKind === 'tv') {
    kind = 'series';
  } else if (title.isScene) {
    kind = 'scene';
  } else if (title.isAdult) {
    kind = 'film';
  }
  return `/catalog/peers/details/${kind}/${title.posterItemId}`;
}

function peerItemDetailsPath(item: PeerCatalogItem) {
  if (item.mediaKind === 'music') {
    return undefined;
  }

  if (item.mediaKind === 'tv') {
    return item.episodeNumbers.length
      ? `/catalog/peers/details/episode/${item.id}?episodeIndex=0`
      : undefined;
  }

  let kind = 'movie';
  if (item.isScene) {
    kind = 'scene';
  } else if (item.isAdult) {
    kind = 'film';
  }
  return `/catalog/peers/details/${kind}/${item.id}`;
}

function peerEpisodeOptions(item: PeerCatalogItem) {
  return item.episodeNumbers.map((number, index) => ({
    path: `/catalog/peers/details/episode/${item.id}?episodeIndex=${index}`,
    label: `E${pad(number)} ${item.episodeNames[index] ?? ''}`.trim(),
  }));
}

function TitleTile({ title }: { title: PeerCatalogTitle }) {
  const counts = STATUSES.filter((s) => title[s] > 0).map(
    (s) => `${title[s]} ${translate(STATUS_LABELS[s])}`
  );
  const detailsPath = peerTitleDetailsPath(title);

  return (
    <div className={styles.tile}>
      {detailsPath ? (
        <CatalogDetailsButton
          path={detailsPath}
          label={describeTitle(title)}
          variant="poster"
        >
          <Poster
            src={peerPosterSrc(title.posterItemId)}
            title={describeTitle(title)}
            size="tile"
            isSquare={title.mediaKind === 'music'}
          />
        </CatalogDetailsButton>
      ) : (
        <Poster
          src={peerPosterSrc(title.posterItemId)}
          title={describeTitle(title)}
          size="tile"
          isSquare={title.mediaKind === 'music'}
        />
      )}

      <div className={styles.tileTitle} title={describeTitle(title)}>
        {detailsPath ? (
          <CatalogDetailsButton
            path={detailsPath}
            label={describeTitle(title)}
            variant="title"
          >
            {describeTitle(title)}
          </CatalogDetailsButton>
        ) : (
          describeTitle(title)
        )}
      </div>

      <div className={styles.tileDetails}>{counts.join(', ')}</div>
      <div className={styles.tileDetails}>{title.peerNames.join(', ')}</div>
      {title.missing + title.upgrade + title.notInLibrary > 0 ? (
        <CatalogGetItButton
          itemId={title.posterItemId}
          scope="title"
          mediaKind={title.mediaKind}
          title={describeTitle(title)}
        />
      ) : null}
    </div>
  );
}

function ToggleButton({
  expanded,
  label,
  id,
  onPress,
}: {
  expanded: boolean;
  label: string;
  id: string;
  onPress: (id: string) => void;
}) {
  const handlePress = useCallback(() => {
    onPress(id);
  }, [id, onPress]);

  return (
    <button
      type="button"
      className={styles.treeToggle}
      aria-label={label}
      aria-expanded={expanded}
      onClick={handlePress}
    >
      {expanded ? '▾' : '▸'}
    </button>
  );
}

function PeerItemRow({ item }: { item: PeerCatalogItem }) {
  const name = describe(item);
  const detailsPath = peerItemDetailsPath(item);
  const options =
    item.mediaKind === 'tv' ? peerEpisodeOptions(item) : undefined;

  return (
    <TableRow>
      <TableRowCell className={styles.posterCell}>
        {detailsPath ? (
          <CatalogDetailsButton
            path={detailsPath}
            label={name}
            variant="poster"
            options={options}
          >
            <Poster
              src={peerPosterSrc(item.id)}
              title={item.title ?? ''}
              size="thumb"
              isSquare={item.mediaKind === 'music'}
            />
          </CatalogDetailsButton>
        ) : (
          <Poster
            src={peerPosterSrc(item.id)}
            title={item.title ?? ''}
            size="thumb"
            isSquare={item.mediaKind === 'music'}
          />
        )}
      </TableRowCell>

      <TableRowCell>{item.peerName}</TableRowCell>

      <TableRowCell className={styles.title}>
        {detailsPath ? (
          <CatalogDetailsButton
            path={detailsPath}
            label={name}
            variant="title"
            options={options}
          >
            {name}
          </CatalogDetailsButton>
        ) : (
          name
        )}
        {item.episodeTitles ? (
          <div className={styles.muted}>
            {options?.length && item.episodeNames.length
              ? options.map((option, index) => (
                  <CatalogDetailsButton
                    key={option.path}
                    path={option.path}
                    label={option.label}
                    variant="episode"
                  >
                    {item.episodeNames[index] || option.label}
                  </CatalogDetailsButton>
                ))
              : item.episodeTitles}
          </div>
        ) : null}
        {item.seriesType === 'daily' && item.airDates.length > 0 ? (
          <div className={styles.muted}>
            {translate('AirDate')}: {item.airDates.join(' + ')}
          </div>
        ) : null}
      </TableRowCell>

      <TableRowCell>
        <Label
          kind={STATUS_KINDS[item.localStatus]}
          title={
            item.localStatus === 'upgrade'
              ? translate('PeerItemUpgradeHelpText')
              : undefined
          }
        >
          {translate(STATUS_LABELS[item.localStatus])}
        </Label>
      </TableRowCell>

      <TableRowCell>
        {item.quality ? <Label>{item.quality}</Label> : null}
        {item.revision ? (
          <Label kind={kinds.PRIMARY}>{item.revision}</Label>
        ) : null}
      </TableRowCell>

      <TableRowCell>{formatBytes(item.size)}</TableRowCell>

      <TableRowCell className={styles.release}>
        {item.releaseTitle}
      </TableRowCell>
      <TableRowCell>
        {item.localStatus === 'have' ? null : (
          <CatalogGetItButton
            itemId={item.id}
            scope="file"
            mediaKind={item.mediaKind}
            title={name}
          />
        )}
      </TableRowCell>
    </TableRow>
  );
}

function PeerCatalog() {
  const [status, setStatus] = useState<PeerItemStatus | undefined>(undefined);
  const [savedKind, setSavedKind] = useState<CatalogKind>(readKind);
  const [page, setPage] = useState(1);
  const [view, setView] = useState<View>(readView);
  const [expandedShows, setExpandedShows] = useState<Set<string>>(new Set());
  const [expandedSeasons, setExpandedSeasons] = useState<Set<string>>(
    new Set()
  );
  const [sortKey, setSortKey] = useState('title');
  const [sortDirection, setSortDirection] =
    useState<SortDirection>('ascending');

  const executeCommand = useExecuteCommand();
  const isSyncing = useCommandExecuting(CommandNames.PeerCatalogSync);

  const {
    data: summaries = [],
    isLoading: isSummaryLoading,
    error: summaryError,
    refetch: refetchSummaries,
  } = usePeerCatalogSummaries(5000);

  const { data: localApplications = [] } = useApiQuery<LocalCatalogSummary[]>({
    path: '/catalog/local',
  });

  const availableKinds = getAvailableCatalogKinds(localApplications);

  const kind = availableKinds.includes(savedKind)
    ? savedKind
    : availableKinds[0] ?? 'series';

  const items = usePagedApiQuery<PeerCatalogItem>({
    path: '/catalog/peers/items',
    page,
    pageSize: PAGE_SIZE,
    sortKey,
    sortDirection,
    queryParams: { ...(status ? { status } : {}), kind },
    queryOptions: {
      enabled: view === 'table' && kind !== 'series',
      refetchInterval: 5000,
    },
  });

  const series = usePagedApiQuery<PeerCatalogSeries>({
    path: '/catalog/peers/series',
    page,
    pageSize: PAGE_SIZE,
    queryParams: status ? { status } : undefined,
    queryOptions: {
      enabled: view === 'table' && kind === 'series',
      refetchInterval: 5000,
    },
  });

  const titles = usePagedApiQuery<PeerCatalogTitle>({
    path: '/catalog/peers/titles',
    page,
    pageSize: POSTER_PAGE_SIZE,
    queryParams: { ...(status ? { status } : {}), kind },
    queryOptions: { enabled: view === 'posters', refetchInterval: 5000 },
  });

  const handleViewPress = useCallback((next: View) => {
    setView(next);
    setPage(1);

    try {
      window.localStorage.setItem(VIEW_KEY, next);
    } catch {
      // Remembered for this visit only.
    }
  }, []);

  const handleTableViewPress = useCallback(() => {
    handleViewPress('table');
  }, [handleViewPress]);

  const handlePosterViewPress = useCallback(() => {
    handleViewPress('posters');
  }, [handleViewPress]);

  let pages = items.totalPages;

  if (view === 'posters') {
    pages = titles.totalPages;
  } else if (kind === 'series') {
    pages = series.totalPages;
  }

  const handleKindPress = useCallback((next: CatalogKind) => {
    setSavedKind(next);
    setPage(1);

    try {
      window.localStorage.setItem(KIND_KEY, next);
    } catch {
      // Remembered for this visit only.
    }
  }, []);

  const handleShowToggle = useCallback((showKey: string) => {
    setExpandedShows((current) => {
      const next = new Set(current);

      if (next.has(showKey)) {
        next.delete(showKey);
      } else {
        next.add(showKey);
      }

      return next;
    });
  }, []);

  const handleSeasonToggle = useCallback((seasonKey: string) => {
    setExpandedSeasons((current) => {
      const next = new Set(current);

      if (next.has(seasonKey)) {
        next.delete(seasonKey);
      } else {
        next.add(seasonKey);
      }

      return next;
    });
  }, []);

  const activeLoading = kind === 'series' ? series.isLoading : items.isLoading;
  const activeError = kind === 'series' ? series.error : items.error;
  const refetchItems = items.refetch;
  const refetchSeries = series.refetch;
  const refetchTitles = titles.refetch;

  const handleSyncPress = useCallback(() => {
    executeCommand({ name: CommandNames.PeerCatalogSync }, () => {
      refetchSummaries();
      refetchItems();
      refetchSeries();
      refetchTitles();
    });
  }, [
    executeCommand,
    refetchSummaries,
    refetchItems,
    refetchSeries,
    refetchTitles,
  ]);

  const handleStatusPress = useCallback((next?: PeerItemStatus) => {
    setStatus(next);
    setPage(1);
  }, []);

  const handleSortPress = useCallback(
    (name: string, direction?: SortDirection) => {
      setSortKey(name);
      setSortDirection(
        direction ??
          (name === sortKey && sortDirection === 'ascending'
            ? 'descending'
            : 'ascending')
      );
      setPage(1);
    },
    [sortKey, sortDirection]
  );

  const handleFirstPagePress = useCallback(() => {
    setPage(1);
  }, []);

  const handlePreviousPagePress = useCallback(() => {
    setPage((current) => Math.max(1, current - 1));
  }, []);

  const handleNextPagePress = useCallback(() => {
    setPage((current) => Math.min(pages, current + 1));
  }, [pages]);

  const handleLastPagePress = useCallback(() => {
    setPage(pages);
  }, [pages]);

  const counts: Record<PeerItemStatus, number> = {
    missing: summaries.reduce((sum, s) => sum + s.missing, 0),
    upgrade: summaries.reduce((sum, s) => sum + s.upgrade, 0),
    notInLibrary: summaries.reduce((sum, s) => sum + s.notInLibrary, 0),
    have: summaries.reduce((sum, s) => sum + s.have, 0),
  };
  const total = STATUSES.reduce((sum, s) => sum + counts[s], 0);

  return (
    <PageContent title={translate('PeerCatalog')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('PeerCatalogSync')}
            iconName={icons.REFRESH}
            isSpinning={isSyncing}
            isDisabled={summaries.length === 0}
            onPress={handleSyncPress}
          />
        </PageToolbarSection>

        <PageToolbarSection alignContent="right">
          <PageToolbarButton
            label={translate('Table')}
            iconName={icons.TABLE}
            isDisabled={view === 'table'}
            onPress={handleTableViewPress}
          />
          <PageToolbarButton
            label={translate('Posters')}
            iconName={icons.POSTER}
            isDisabled={view === 'posters'}
            onPress={handlePosterViewPress}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        {isSummaryLoading ? <LoadingIndicator /> : null}

        {summaryError || activeError ? (
          <Alert kind={kinds.DANGER}>{translate('CatalogLoadError')}</Alert>
        ) : null}

        {!isSummaryLoading && !summaryError && summaries.length === 0 ? (
          <Alert kind={kinds.INFO}>{translate('PeerCatalogNoPeers')}</Alert>
        ) : null}

        <div className={styles.summary}>
          {summaries.map((summary) => (
            <div key={summary.peerId} className={styles.card}>
              <div className={styles.cardName}>{summary.peerName}</div>

              <div className={styles.muted}>
                {STATUSES.map(
                  (s) => `${summary[s]} ${translate(STATUS_LABELS[s])}`
                ).join(', ')}
              </div>

              <div className={styles.muted}>
                {translate('CatalogLastSync')}:{' '}
                {summary.catalogSyncedAt
                  ? formatDateTime(
                      summary.catalogSyncedAt,
                      'MMM D YYYY',
                      'h:mma'
                    )
                  : translate('CatalogNeverSynced')}
              </div>

              {summary.catalogError ? (
                <div className={styles.error}>{summary.catalogError}</div>
              ) : null}
            </div>
          ))}
        </div>

        {summaries.length > 0 ? (
          <div className={styles.buttons}>
            <StatusButton
              count={total}
              isSelected={!status}
              onPress={handleStatusPress}
            />

            {STATUSES.map((s) => (
              <StatusButton
                key={s}
                status={s}
                count={counts[s]}
                isSelected={status === s}
                onPress={handleStatusPress}
              />
            ))}
          </div>
        ) : null}

        {summaries.length > 0 && total === 0 ? (
          <Alert kind={kinds.INFO}>
            {translate('PeerCatalogNothingShared')}
          </Alert>
        ) : null}

        {availableKinds.length > 0 ? (
          <div className={styles.buttons}>
            {availableKinds.map((availableKind) => (
              <CatalogKindButton
                key={availableKind}
                kind={availableKind}
                isSelected={kind === availableKind}
                onPress={handleKindPress}
              />
            ))}
          </div>
        ) : null}

        {view === 'posters' && titles.isLoading ? <LoadingIndicator /> : null}

        {view === 'posters' &&
        !titles.isLoading &&
        titles.records.length > 0 ? (
          <div>
            <div className={styles.tiles}>
              {titles.records.map((title) => (
                <TitleTile key={title.key} title={title} />
              ))}
            </div>

            <TablePager
              page={page}
              totalPages={titles.totalPages}
              totalRecords={titles.totalRecords}
              isFetching={titles.isFetching}
              onFirstPagePress={handleFirstPagePress}
              onPreviousPagePress={handlePreviousPagePress}
              onNextPagePress={handleNextPagePress}
              onLastPagePress={handleLastPagePress}
              onPageSelect={setPage}
            />
          </div>
        ) : null}

        {view === 'table' && activeLoading ? <LoadingIndicator /> : null}

        {view === 'table' &&
        kind === 'series' &&
        !series.isLoading &&
        series.records.length > 0 ? (
          <div>
            <Table
              columns={COLUMNS}
              canModifyColumns={false}
              sortKey="title"
              sortDirection="ascending"
            >
              <TableBody>
                {series.records.map((show) => {
                  const showExpanded = expandedShows.has(show.key);
                  const peers = Array.from(
                    new Set(
                      show.seasons
                        .flatMap((season) => season.items)
                        .map((item) => item.peerName)
                        .filter(Boolean)
                    )
                  );

                  return (
                    <React.Fragment key={show.key}>
                      <TableRow className={styles.treeShowRow}>
                        <TableRowCell className={styles.posterCell}>
                          <CatalogDetailsButton
                            path={`/catalog/peers/details/series/${show.posterItemId}`}
                            label={show.title ?? ''}
                            variant="poster"
                          >
                            <Poster
                              src={peerPosterSrc(show.posterItemId)}
                              title={show.title ?? ''}
                              size="thumb"
                            />
                          </CatalogDetailsButton>
                        </TableRowCell>
                        <TableRowCell>{peers.join(', ')}</TableRowCell>
                        <TableRowCell className={styles.title}>
                          <div className={styles.treeTitle}>
                            <ToggleButton
                              expanded={showExpanded}
                              label={`${showExpanded ? 'Collapse' : 'Expand'} ${
                                show.title
                              }`}
                              id={show.key}
                              onPress={handleShowToggle}
                            />
                            <CatalogDetailsButton
                              path={`/catalog/peers/details/series/${show.posterItemId}`}
                              label={show.title ?? ''}
                              variant="title"
                            >
                              <strong>
                                {titleWithYear(show.title, show.year)}
                              </strong>
                            </CatalogDetailsButton>
                          </div>
                          <div className={styles.muted}>
                            {translate('CatalogSeriesSummary', {
                              count: show.episodes,
                              missing: show.missing,
                              upgrade: show.upgrade,
                            })}
                          </div>
                        </TableRowCell>
                        <TableRowCell>
                          <Label kind={STATUS_KINDS[show.status]}>
                            {translate(STATUS_LABELS[show.status])}
                          </Label>
                        </TableRowCell>
                        <TableRowCell />
                        <TableRowCell>{formatBytes(show.size)}</TableRowCell>
                        <TableRowCell />
                        <TableRowCell>
                          {show.status === 'have' ? null : (
                            <CatalogGetItButton
                              itemId={show.posterItemId}
                              scope="title"
                              mediaKind="tv"
                              title={titleWithYear(show.title, show.year)}
                            />
                          )}
                        </TableRowCell>
                      </TableRow>

                      {showExpanded
                        ? show.seasons.map((season) => {
                            const seasonKey = `${show.key}-${season.seasonNumber}`;
                            const seasonExpanded =
                              expandedSeasons.has(seasonKey);

                            return (
                              <React.Fragment key={seasonKey}>
                                <TableRow className={styles.treeSeasonRow}>
                                  <TableRowCell />
                                  <TableRowCell />
                                  <TableRowCell className={styles.title}>
                                    <div className={styles.treeTitle}>
                                      <ToggleButton
                                        expanded={seasonExpanded}
                                        label={`${
                                          seasonExpanded ? 'Collapse' : 'Expand'
                                        } ${translate('Season')} ${
                                          season.seasonNumber
                                        }`}
                                        id={seasonKey}
                                        onPress={handleSeasonToggle}
                                      />
                                      <strong>
                                        {translate('Season')}{' '}
                                        {season.seasonNumber}
                                      </strong>
                                    </div>
                                    <div className={styles.muted}>
                                      {translate('CatalogSeriesSummary', {
                                        count: season.episodes,
                                        missing: season.missing,
                                        upgrade: season.upgrade,
                                      })}
                                    </div>
                                  </TableRowCell>
                                  <TableRowCell>
                                    <Label kind={STATUS_KINDS[season.status]}>
                                      {translate(STATUS_LABELS[season.status])}
                                    </Label>
                                  </TableRowCell>
                                  <TableRowCell />
                                  <TableRowCell>
                                    {formatBytes(season.size)}
                                  </TableRowCell>
                                  <TableRowCell />
                                  <TableRowCell />
                                </TableRow>
                                {seasonExpanded
                                  ? season.items.map((item) => (
                                      <PeerItemRow key={item.id} item={item} />
                                    ))
                                  : null}
                              </React.Fragment>
                            );
                          })
                        : null}
                    </React.Fragment>
                  );
                })}
              </TableBody>
            </Table>

            <TablePager
              page={page}
              totalPages={series.totalPages}
              totalRecords={series.totalRecords}
              isFetching={series.isFetching}
              onFirstPagePress={handleFirstPagePress}
              onPreviousPagePress={handlePreviousPagePress}
              onNextPagePress={handleNextPagePress}
              onLastPagePress={handleLastPagePress}
              onPageSelect={setPage}
            />
          </div>
        ) : null}

        {view === 'table' &&
        kind !== 'series' &&
        !items.isLoading &&
        items.records.length > 0 ? (
          <div>
            <Table
              columns={COLUMNS}
              canModifyColumns={false}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSortPress={handleSortPress}
            >
              <TableBody>
                {items.records.map((item) => (
                  <PeerItemRow key={item.id} item={item} />
                ))}
              </TableBody>
            </Table>

            <TablePager
              page={page}
              totalPages={items.totalPages}
              totalRecords={items.totalRecords}
              isFetching={items.isFetching}
              onFirstPagePress={handleFirstPagePress}
              onPreviousPagePress={handlePreviousPagePress}
              onNextPagePress={handleNextPagePress}
              onLastPagePress={handleLastPagePress}
              onPageSelect={setPage}
            />
          </div>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default PeerCatalog;
