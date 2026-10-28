import React, { useCallback, useEffect, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
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
import {
  CatalogKind,
  LocalCatalogFile,
  LocalCatalogSeries,
  LocalCatalogSummary,
  LocalCatalogTitle,
} from 'typings/LocalCatalog';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatBytes from 'Utilities/Number/formatBytes';
import titleWithYear, { yearOnItsOwn } from 'Utilities/String/titleWithYear';
import translate from 'Utilities/String/translate';
import CatalogAvailability from '../CatalogAvailability';
import CatalogKindButton from '../CatalogKindButton';
import { getAvailableCatalogKinds } from '../catalogKinds';
import CatalogDetailsButton from '../Details/CatalogDetailsButton';
import Poster, { localPosterSrc } from '../Poster';
import SendFromCatalogButton from '../Sharing/SendFromCatalogButton';
import useCatalogMissingEpisodes from '../useCatalogMissingEpisodes';
import { ShareKey, TitleKey } from '../useSharing';
import catalogStyles from '../Catalog.css';
import styles from './LocalCatalog.css';

const PAGE_SIZE = 50;

// A grid of posters fills a page with fewer, larger items.
const POSTER_PAGE_SIZE = 48;

type View = 'table' | 'posters';

const VIEW_KEY = 'nodearr.localCatalog.view';
const KIND_KEY = 'nodearr.localCatalog.kind';

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
  {
    name: 'applicationName',
    label: () => translate('SourceApplication'),
    isVisible: true,
  },
  { name: 'title', label: () => translate('Title'), isVisible: true },
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
    name: 'path',
    label: () => translate('Path'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'readable',
    label: () => translate('CatalogReadable'),
    isVisible: true,
  },
  {
    name: 'dateAdded',
    label: () => translate('Added'),
    isSortable: true,
    isVisible: true,
  },
  { name: 'send', label: () => translate('Send'), isVisible: true },
];

function pad(value: number) {
  return value.toString().padStart(2, '0');
}

function describe(file: LocalCatalogFile) {
  if (file.mediaType === 'track') {
    return `${file.artist ?? ''} - ${file.albumTitle ?? file.title ?? ''} - ${
      file.trackTitle ?? ''
    }`;
  }

  if (file.mediaType === 'movie') {
    return titleWithYear(file.title, file.year);
  }

  const episodes =
    file.seriesType === 'anime' && file.absoluteEpisodeNumbers.length > 0
      ? file.absoluteEpisodeNumbers.map((n) => `#${pad(n)}`).join(' + ')
      : file.episodeNumbers.map((n) => `E${pad(n)}`).join('');
  const dailyDate = file.seriesType === 'daily' ? file.airDates[0] : undefined;
  const season = `S${pad(file.seasonNumber ?? 0)}`;

  return `${file.title} - ${season}${
    dailyDate ? ` ${dailyDate}` : ''
  }${episodes}`;
}

function titleKey(title: LocalCatalogTitle): TitleKey {
  if (title.mediaType === 'series') {
    return {
      mediaKind: 'tv',
      applicationId: title.applicationId,
      arrId: title.posterArrId,
    };
  }

  if (title.mediaType === 'album') {
    return {
      mediaKind: 'music',
      applicationId: title.applicationId,
      arrId: title.artistArrId,
    };
  }

  return {
    mediaKind: 'movie',
    applicationId: title.applicationId,
    arrId: title.posterArrId,
  };
}

function titleItems(title: LocalCatalogTitle): ShareKey[] {
  const key = titleKey(title);

  return title.mediaType === 'album'
    ? [{ ...key, albumArrId: title.posterArrId }]
    : [key];
}

function titleDetailsPath(title: LocalCatalogTitle) {
  if (title.mediaType === 'album') {
    return undefined;
  }

  let kind = 'movie';
  if (title.mediaType === 'series') {
    kind = 'series';
  } else if (title.kind === 'scenes') {
    kind = 'scene';
  } else if (title.isAdult) {
    kind = 'film';
  }

  return `/catalog/local/details/${kind}/${title.applicationId}/${title.posterArrId}`;
}

function fileDetailsPath(file: LocalCatalogFile) {
  if (file.mediaType === 'episode') {
    const episodeId = file.episodeArrIds[0];
    return episodeId
      ? `/catalog/local/details/episode/${file.applicationId}/${episodeId}`
      : undefined;
  }

  if (file.mediaType === 'movie') {
    let kind = 'movie';
    if (file.isScene) {
      kind = 'scene';
    } else if (file.isAdult) {
      kind = 'film';
    }
    return `/catalog/local/details/${kind}/${file.applicationId}/${file.posterArrId}`;
  }

  return undefined;
}

function episodeDetailsOptions(file: LocalCatalogFile) {
  return file.episodeArrIds.map((id, index) => ({
    path: `/catalog/local/details/episode/${file.applicationId}/${id}`,
    label: `E${pad(file.episodeNumbers[index] ?? index + 1)} ${
      file.episodeTitles[index] ?? ''
    }`.trim(),
  }));
}

function fileTitleKey(file: LocalCatalogFile): TitleKey {
  if (file.mediaType === 'episode') {
    return {
      mediaKind: 'tv',
      applicationId: file.applicationId,
      arrId: file.posterArrId,
    };
  }

  if (file.mediaType === 'track') {
    return {
      mediaKind: 'music',
      applicationId: file.applicationId,
      arrId: file.artistArrId,
    };
  }

  return {
    mediaKind: 'movie',
    applicationId: file.applicationId,
    arrId: file.posterArrId,
  };
}

function fileItems(file: LocalCatalogFile): ShareKey[] {
  const key = fileTitleKey(file);

  if (file.mediaType === 'episode') {
    return file.episodeArrIds.map((episodeArrId) => ({ ...key, episodeArrId }));
  }

  if (file.mediaType === 'track') {
    return [{ ...key, albumArrId: file.posterArrId }];
  }

  return [key];
}

function TitleTile({ title }: { title: LocalCatalogTitle }) {
  const heading = title.artist
    ? `${title.artist} - ${title.title}`
    : title.title;
  const name = titleWithYear(heading, title.year);
  const year = yearOnItsOwn(heading, title.year);
  const shown = year ? heading : name;
  const detailsPath = titleDetailsPath(title);

  return (
    <div className={catalogStyles.tile}>
      <div className={catalogStyles.tilePoster}>
        {detailsPath ? (
          <CatalogDetailsButton
            path={detailsPath}
            label={name}
            variant="poster"
          >
            <Poster
              src={localPosterSrc(
                title.applicationId,
                title.posterSubject,
                title.posterArrId
              )}
              title={name}
              size="tile"
              isSquare={title.kind === 'music'}
            />
          </CatalogDetailsButton>
        ) : (
          <Poster
            src={localPosterSrc(
              title.applicationId,
              title.posterSubject,
              title.posterArrId
            )}
            title={name}
            size="tile"
            isSquare={title.kind === 'music'}
          />
        )}
        <SendFromCatalogButton
          className={catalogStyles.tileSend}
          titleKey={titleKey(title)}
          items={titleItems(title)}
          label={name}
        />
      </div>

      <div className={catalogStyles.tileTitle} title={name}>
        {detailsPath ? (
          <CatalogDetailsButton path={detailsPath} label={name} variant="title">
            {shown}
          </CatalogDetailsButton>
        ) : (
          shown
        )}
      </div>
      {year ? <div className={catalogStyles.tileDetails}>{year}</div> : null}

      <CatalogAvailability status="have">
        {title.files === 1
          ? translate('SharingOneFile')
          : translate('SharingFileCount', { count: title.files })}
      </CatalogAvailability>
      <div className={catalogStyles.tileDetails}>{title.applicationName}</div>
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

function sceneLabel(file: LocalCatalogFile) {
  if (
    file.sceneSeasonNumber === undefined ||
    file.sceneEpisodeNumbers.length === 0
  ) {
    return undefined;
  }

  const differs =
    file.sceneSeasonNumber !== file.seasonNumber ||
    file.sceneEpisodeNumbers.some(
      (number, index) => number !== file.episodeNumbers[index]
    );

  return differs
    ? `Scene S${pad(file.sceneSeasonNumber)}${file.sceneEpisodeNumbers
        .map((number) => `E${pad(number)}`)
        .join('')}`
    : undefined;
}

function LocalFileRow({ file }: { file: LocalCatalogFile }) {
  const alternateNumbers = sceneLabel(file);
  const missing = file.isMissing || Boolean(file.missingSince);
  const detailsPath = fileDetailsPath(file);
  const options =
    file.mediaType === 'episode' ? episodeDetailsOptions(file) : undefined;

  return (
    <TableRow>
      <TableRowCell className={catalogStyles.posterCell}>
        {detailsPath ? (
          <CatalogDetailsButton
            path={detailsPath}
            label={describe(file)}
            variant="poster"
            options={options}
          >
            <Poster
              src={localPosterSrc(
                file.applicationId,
                file.posterSubject,
                file.posterArrId
              )}
              title={file.title ?? ''}
              size="thumb"
              isSquare={file.mediaType === 'track'}
            />
          </CatalogDetailsButton>
        ) : (
          <Poster
            src={localPosterSrc(
              file.applicationId,
              file.posterSubject,
              file.posterArrId
            )}
            title={file.title ?? ''}
            size="thumb"
            isSquare={file.mediaType === 'track'}
          />
        )}
      </TableRowCell>

      <TableRowCell>{file.applicationName}</TableRowCell>

      <TableRowCell className={styles.title}>
        {detailsPath ? (
          <CatalogDetailsButton
            path={detailsPath}
            label={describe(file)}
            variant="title"
            options={options}
          >
            {describe(file)}
          </CatalogDetailsButton>
        ) : (
          describe(file)
        )}
        {file.episodeTitles.length > 0 ? (
          <div className={styles.episodeTitles}>
            {options?.length
              ? options.map((option, index) => (
                  <CatalogDetailsButton
                    key={option.path}
                    path={option.path}
                    label={option.label}
                    variant="episode"
                  >
                    {file.episodeTitles[index] || option.label}
                  </CatalogDetailsButton>
                ))
              : file.episodeTitles.join(' + ')}
          </div>
        ) : null}
        {file.seriesType === 'daily' && file.airDates.length > 0 ? (
          <div className={styles.episodeTitles}>
            {translate('AirDate')}: {file.airDates.join(' + ')}
          </div>
        ) : null}
        {alternateNumbers ? (
          <div className={styles.episodeTitles}>{alternateNumbers}</div>
        ) : null}
      </TableRowCell>

      <TableRowCell>
        {file.quality ? <Label>{file.quality}</Label> : null}
        {file.revision ? (
          <Label kind={kinds.PRIMARY}>{file.revision}</Label>
        ) : null}
      </TableRowCell>

      <TableRowCell>{formatBytes(file.size)}</TableRowCell>

      <TableRowCell className={styles.path} title={file.localPath ?? file.path}>
        {file.path}
      </TableRowCell>

      <TableRowCell>
        <CatalogAvailability status={missing ? 'missing' : 'have'} />
        {missing ? null : (
          <Icon
            name={file.readable ? icons.CHECK : icons.WARNING}
            kind={file.readable ? kinds.SUCCESS : kinds.WARNING}
            title={
              file.readable
                ? file.localPath
                : `${translate(
                    file.access === 'notAllowed'
                      ? 'CatalogFileNotAllowed'
                      : 'CatalogFileMissing'
                  )}: ${file.localPath}`
            }
          />
        )}
      </TableRowCell>

      <RelativeDateCell date={file.dateAdded} />

      <TableRowCell>
        {file.readable && !missing && fileItems(file).length ? (
          <SendFromCatalogButton
            titleKey={fileTitleKey(file)}
            items={fileItems(file)}
            label={describe(file)}
          />
        ) : null}
      </TableRowCell>
    </TableRow>
  );
}

// What an app's library holds, in its own terms.
function countsText(application: LocalCatalogSummary) {
  switch (application.implementation) {
    case 'Radarr':
    case 'WhisparrV3':
    case 'WhisparrV2':
      return `${application.movies} ${translate('Movies')}, ${
        application.movieFiles
      } ${translate('MovieFiles')}`;
    case 'Lidarr':
      return `${application.artists} ${translate('Artists')}, ${
        application.albums
      } ${translate('Albums')}, ${application.trackFiles} ${translate(
        'TrackFiles'
      )}`;
    default:
      return `${application.series} ${translate('Series')}, ${
        application.episodes
      } ${translate('Episodes')}, ${application.episodeFiles} ${translate(
        'EpisodeFiles'
      )}`;
  }
}

function catalogFilePath(kind: CatalogKind) {
  if (kind === 'movies' || kind === 'scenes') {
    return '/catalog/local/moviefile';
  }

  return kind === 'music'
    ? '/catalog/local/trackfile'
    : '/catalog/local/episodefile';
}

function LocalCatalog() {
  const [savedKind, setSavedKind] = useState<CatalogKind>(readKind);
  const [page, setPage] = useState(1);
  const [view, setView] = useState<View>(readView);
  const [showMissing, setShowMissing] = useCatalogMissingEpisodes(false);
  const [expandedShows, setExpandedShows] = useState<Set<string>>(new Set());
  const [expandedSeasons, setExpandedSeasons] = useState<Set<string>>(
    new Set()
  );
  const [sortKey, setSortKey] = useState('path');
  const [sortDirection, setSortDirection] = useState<SortDirection>(
    'ascending' as SortDirection
  );

  useEffect(() => {
    setPage(1);
  }, [showMissing]);

  const executeCommand = useExecuteCommand();
  const isRefreshing = useCommandExecuting(CommandNames.CatalogRefresh);

  const {
    data: summary = [],
    isLoading: isSummaryLoading,
    error: summaryError,
    refetch: refetchSummary,
  } = useApiQuery<LocalCatalogSummary[]>({ path: '/catalog/local' });

  const availableKinds = getAvailableCatalogKinds(summary);

  const kind = availableKinds.includes(savedKind)
    ? savedKind
    : availableKinds[0] ?? 'series';

  const files = usePagedApiQuery<LocalCatalogFile>({
    path: catalogFilePath(kind),
    page,
    pageSize: PAGE_SIZE,
    sortKey,
    sortDirection,
    queryParams: kind === 'movies' || kind === 'scenes' ? { kind } : undefined,
    queryOptions: { enabled: view === 'table' && kind !== 'series' },
  });

  const series = usePagedApiQuery<LocalCatalogSeries>({
    path: '/catalog/local/series',
    page,
    pageSize: PAGE_SIZE,
    sortKey: 'title',
    sortDirection: 'ascending',
    queryParams: { onlyWithFiles: !showMissing },
    queryOptions: { enabled: view === 'table' && kind === 'series' },
  });

  const titles = usePagedApiQuery<LocalCatalogTitle>({
    path: '/catalog/local/titles',
    page,
    pageSize: POSTER_PAGE_SIZE,
    queryParams: { kind },
    queryOptions: { enabled: view === 'posters' },
  });

  let pages = files.totalPages;

  if (view === 'posters') {
    pages = titles.totalPages;
  } else if (kind === 'series') {
    pages = series.totalPages;
  }

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

  const refetchTitles = titles.refetch;
  const refetchFiles = files.refetch;
  const refetchSeries = series.refetch;

  const reload = useCallback(() => {
    refetchSummary();
    refetchFiles();
    refetchSeries();
    refetchTitles();
  }, [refetchSummary, refetchFiles, refetchSeries, refetchTitles]);

  const handleRefreshPress = useCallback(() => {
    executeCommand({ name: CommandNames.CatalogRefresh }, reload);
  }, [executeCommand, reload]);

  const handlePurgePress = useCallback(() => {
    executeCommand(
      { name: CommandNames.CatalogRefresh, purgeMissing: true },
      reload
    );
  }, [executeCommand, reload]);

  const handleKindPress = useCallback((next: CatalogKind) => {
    setSavedKind(next);
    setPage(1);

    try {
      window.localStorage.setItem(KIND_KEY, next);
    } catch {
      // Remembered for this visit only.
    }
  }, []);

  const handleShowMissingChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setShowMissing(event.target.checked);
      setPage(1);
    },
    [setShowMissing]
  );

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

  const activeLoading = kind === 'series' ? series.isLoading : files.isLoading;
  const activeError = kind === 'series' ? series.error : files.error;

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

  const handleSortPress = useCallback(
    (name: string, direction?: SortDirection) => {
      const next =
        direction ??
        ((name === sortKey && sortDirection === 'ascending'
          ? 'descending'
          : 'ascending') as SortDirection);

      setSortKey(name);
      setSortDirection(next);
      setPage(1);
    },
    [sortKey, sortDirection]
  );

  const missingCount = summary.reduce((sum, a) => sum + a.missingFiles, 0);

  return (
    <PageContent title={translate('LocalCatalog')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('CatalogRefresh')}
            iconName={icons.REFRESH}
            isSpinning={isRefreshing}
            isDisabled={summary.length === 0}
            onPress={handleRefreshPress}
          />

          <PageToolbarButton
            label={translate('CatalogPurgeMissing')}
            iconName={icons.DELETE}
            isDisabled={isRefreshing || missingCount === 0}
            onPress={handlePurgePress}
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

        {!isSummaryLoading && !summaryError && summary.length === 0 ? (
          <Alert kind={kinds.INFO}>{translate('CatalogEmpty')}</Alert>
        ) : null}

        <div className={styles.summary}>
          {summary.map((application) => (
            <div key={application.applicationId} className={styles.application}>
              <div className={styles.applicationName}>
                {application.applicationName}
              </div>

              <div className={styles.counts}>{countsText(application)}</div>

              <div className={styles.counts}>
                {translate('CatalogLastSync')}:{' '}
                {application.lastSyncCompleted
                  ? formatDateTime(
                      application.lastSyncCompleted,
                      'MMM D YYYY',
                      'h:mma'
                    )
                  : translate('CatalogNeverSynced')}
                {application.missingFiles > 0 ? (
                  <>
                    {' '}
                    <CatalogAvailability status="missing">
                      {application.missingFiles} {translate('Missing')}
                    </CatalogAvailability>
                  </>
                ) : null}
              </div>

              {application.lastSyncError ? (
                <div className={styles.syncError}>
                  {application.lastSyncError}
                </div>
              ) : null}
            </div>
          ))}
        </div>

        {availableKinds.length > 0 ? (
          <div className={styles.mediaTypes}>
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

        {view === 'table' && kind === 'series' ? (
          <label className={styles.missingFilter}>
            <input
              type="checkbox"
              checked={showMissing}
              onChange={handleShowMissingChange}
            />
            {translate('CatalogShowMissingEpisodes')}
          </label>
        ) : null}

        {view === 'posters' && titles.isLoading ? <LoadingIndicator /> : null}

        {view === 'posters' &&
        !titles.isLoading &&
        titles.records.length > 0 ? (
          <div>
            <div className={catalogStyles.tiles}>
              {titles.records.map((title) => (
                <TitleTile
                  key={`${title.applicationId}-${title.posterSubject}-${title.posterArrId}`}
                  title={title}
                />
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
        !showMissing &&
        !series.isLoading &&
        !series.error &&
        series.records.length === 0 ? (
          <Alert kind={kinds.INFO}>
            {translate('CatalogNoDownloadedEpisodes')}
          </Alert>
        ) : null}

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
                  const showKey = `${show.applicationId}-${show.posterArrId}`;
                  const showExpanded = expandedShows.has(showKey);

                  return (
                    <React.Fragment key={showKey}>
                      <TableRow className={styles.treeShowRow}>
                        <TableRowCell className={catalogStyles.posterCell}>
                          <CatalogDetailsButton
                            path={`/catalog/local/details/series/${show.applicationId}/${show.posterArrId}`}
                            label={show.title ?? ''}
                            variant="poster"
                          >
                            <Poster
                              src={localPosterSrc(
                                show.applicationId,
                                'series',
                                show.posterArrId
                              )}
                              title={show.title ?? ''}
                              size="thumb"
                            />
                          </CatalogDetailsButton>
                        </TableRowCell>
                        <TableRowCell>{show.applicationName}</TableRowCell>
                        <TableRowCell className={styles.title}>
                          <div className={styles.treeTitle}>
                            <ToggleButton
                              expanded={showExpanded}
                              label={`${showExpanded ? 'Collapse' : 'Expand'} ${
                                show.title
                              }`}
                              id={showKey}
                              onPress={handleShowToggle}
                            />
                            <CatalogDetailsButton
                              path={`/catalog/local/details/series/${show.applicationId}/${show.posterArrId}`}
                              label={show.title ?? ''}
                              variant="title"
                            >
                              <strong>
                                {titleWithYear(show.title, show.year)}
                              </strong>
                            </CatalogDetailsButton>
                          </div>
                          <div className={styles.episodeTitles}>
                            {translate('CatalogLocalSeriesSummary', {
                              count: show.episodes,
                              missing: show.missing,
                            })}
                          </div>
                        </TableRowCell>
                        <TableRowCell />
                        <TableRowCell>{formatBytes(show.size)}</TableRowCell>
                        <TableRowCell />
                        <TableRowCell>
                          <CatalogAvailability status="have">
                            {show.episodes - show.missing}{' '}
                            {translate('PeerItemHave')}
                          </CatalogAvailability>
                          {show.missing > 0 ? (
                            <CatalogAvailability status="missing">
                              {show.missing} {translate('Missing')}
                            </CatalogAvailability>
                          ) : null}
                        </TableRowCell>
                        <TableRowCell />
                        <TableRowCell>
                          <SendFromCatalogButton
                            titleKey={{
                              mediaKind: 'tv',
                              applicationId: show.applicationId,
                              arrId: show.posterArrId,
                            }}
                            items={[
                              {
                                mediaKind: 'tv',
                                applicationId: show.applicationId,
                                arrId: show.posterArrId,
                              },
                            ]}
                            label={show.title ?? ''}
                          />
                        </TableRowCell>
                      </TableRow>

                      {showExpanded
                        ? show.seasons.map((season) => {
                            const seasonKey = `${showKey}-${season.seasonNumber}`;
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
                                    <div className={styles.episodeTitles}>
                                      {translate('CatalogLocalSeriesSummary', {
                                        count: season.episodes,
                                        missing: season.missing,
                                      })}
                                    </div>
                                  </TableRowCell>
                                  <TableRowCell />
                                  <TableRowCell>
                                    {formatBytes(season.size)}
                                  </TableRowCell>
                                  <TableRowCell />
                                  <TableRowCell>
                                    <CatalogAvailability status="have">
                                      {season.episodes - season.missing}{' '}
                                      {translate('PeerItemHave')}
                                    </CatalogAvailability>
                                    {season.missing > 0 ? (
                                      <CatalogAvailability status="missing">
                                        {season.missing} {translate('Missing')}
                                      </CatalogAvailability>
                                    ) : null}
                                  </TableRowCell>
                                  <TableRowCell />
                                  <TableRowCell>
                                    <SendFromCatalogButton
                                      titleKey={{
                                        mediaKind: 'tv',
                                        applicationId: show.applicationId,
                                        arrId: show.posterArrId,
                                      }}
                                      items={[
                                        {
                                          mediaKind: 'tv',
                                          applicationId: show.applicationId,
                                          arrId: show.posterArrId,
                                          seasonNumber: season.seasonNumber,
                                        },
                                      ]}
                                      label={`${show.title ?? ''} ${translate(
                                        'Season'
                                      )} ${season.seasonNumber}`}
                                    />
                                  </TableRowCell>
                                </TableRow>
                                {seasonExpanded
                                  ? season.files.map((file) => (
                                      <LocalFileRow key={file.id} file={file} />
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
        !files.isLoading &&
        files.records.length > 0 ? (
          <div>
            <Table
              columns={COLUMNS}
              canModifyColumns={false}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSortPress={handleSortPress}
            >
              <TableBody>
                {files.records.map((file) => (
                  <LocalFileRow key={file.id} file={file} />
                ))}
              </TableBody>
            </Table>

            <TablePager
              page={page}
              totalPages={files.totalPages}
              totalRecords={files.totalRecords}
              isFetching={files.isFetching}
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

export default LocalCatalog;
