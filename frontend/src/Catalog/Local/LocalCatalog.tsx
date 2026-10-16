import React, { useCallback, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
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
  LocalCatalogFile,
  LocalCatalogMediaType,
  LocalCatalogSummary,
} from 'typings/LocalCatalog';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './LocalCatalog.css';

const PAGE_SIZE = 50;

const COLUMNS: Column[] = [
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
];

function pad(value: number) {
  return value.toString().padStart(2, '0');
}

function describe(file: LocalCatalogFile) {
  if (file.mediaType === 'movie') {
    return file.year ? `${file.title} (${file.year})` : file.title;
  }

  const episodes = file.episodeNumbers.map((n) => `E${pad(n)}`).join('');

  return `${file.title} - S${pad(file.seasonNumber ?? 0)}${episodes}`;
}

function LocalCatalog() {
  const [mediaType, setMediaType] = useState<LocalCatalogMediaType>('episode');
  const [page, setPage] = useState(1);
  const [sortKey, setSortKey] = useState('path');
  const [sortDirection, setSortDirection] = useState<SortDirection>(
    'ascending' as SortDirection
  );

  const executeCommand = useExecuteCommand();
  const isRefreshing = useCommandExecuting(CommandNames.CatalogRefresh);

  const {
    data: summary = [],
    isLoading: isSummaryLoading,
    error: summaryError,
    refetch: refetchSummary,
  } = useApiQuery<LocalCatalogSummary[]>({ path: '/catalog/local' });

  const {
    records,
    totalPages,
    totalRecords,
    isFetching,
    isLoading,
    error,
    refetch,
  } = usePagedApiQuery<LocalCatalogFile>({
    path:
      mediaType === 'movie'
        ? '/catalog/local/moviefile'
        : '/catalog/local/episodefile',
    page,
    pageSize: PAGE_SIZE,
    sortKey,
    sortDirection,
  });

  const reload = useCallback(() => {
    refetchSummary();
    refetch();
  }, [refetchSummary, refetch]);

  const handleRefreshPress = useCallback(() => {
    executeCommand({ name: CommandNames.CatalogRefresh }, reload);
  }, [executeCommand, reload]);

  const handlePurgePress = useCallback(() => {
    executeCommand(
      { name: CommandNames.CatalogRefresh, purgeMissing: true },
      reload
    );
  }, [executeCommand, reload]);

  const handleEpisodeFilesPress = useCallback(() => {
    setMediaType('episode');
    setPage(1);
  }, []);

  const handleMovieFilesPress = useCallback(() => {
    setMediaType('movie');
    setPage(1);
  }, []);

  const handleFirstPagePress = useCallback(() => {
    setPage(1);
  }, []);

  const handlePreviousPagePress = useCallback(() => {
    setPage((current) => Math.max(1, current - 1));
  }, []);

  const handleNextPagePress = useCallback(() => {
    setPage((current) => Math.min(totalPages, current + 1));
  }, [totalPages]);

  const handleLastPagePress = useCallback(() => {
    setPage(totalPages);
  }, [totalPages]);

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

  const episodeFileCount = summary.reduce((sum, a) => sum + a.episodeFiles, 0);
  const movieFileCount = summary.reduce((sum, a) => sum + a.movieFiles, 0);
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
      </PageToolbar>

      <PageContentBody>
        {isSummaryLoading ? <LoadingIndicator /> : null}

        {summaryError || error ? (
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

              <div className={styles.counts}>
                {application.implementation === 'Radarr' ||
                application.implementation === 'WhisparrV3'
                  ? `${application.movies} ${translate('Movies')}, ${
                      application.movieFiles
                    } ${translate('MovieFiles')}`
                  : `${application.series} ${translate('Series')}, ${
                      application.episodes
                    } ${translate('Episodes')}, ${
                      application.episodeFiles
                    } ${translate('EpisodeFiles')}`}
              </div>

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
                    <Label kind={kinds.WARNING}>
                      {application.missingFiles} {translate('Missing')}
                    </Label>
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

        {summary.length > 0 ? (
          <div className={styles.mediaTypes}>
            <Button
              kind={mediaType === 'episode' ? kinds.PRIMARY : kinds.DEFAULT}
              onPress={handleEpisodeFilesPress}
            >
              {translate('EpisodeFiles')} ({episodeFileCount})
            </Button>

            <Button
              kind={mediaType === 'movie' ? kinds.PRIMARY : kinds.DEFAULT}
              onPress={handleMovieFilesPress}
            >
              {translate('MovieFiles')} ({movieFileCount})
            </Button>
          </div>
        ) : null}

        {isLoading ? <LoadingIndicator /> : null}

        {!isLoading && records.length > 0 ? (
          <div>
            <Table
              columns={COLUMNS}
              canModifyColumns={false}
              sortKey={sortKey}
              sortDirection={sortDirection}
              onSortPress={handleSortPress}
            >
              <TableBody>
                {records.map((file) => (
                  <TableRow
                    key={file.id}
                    className={file.missingSince ? styles.missing : undefined}
                  >
                    <TableRowCell>{file.applicationName}</TableRowCell>

                    <TableRowCell className={styles.title}>
                      {describe(file)}
                      {file.episodeTitles.length > 0 ? (
                        <div className={styles.episodeTitles}>
                          {file.episodeTitles.join(' + ')}
                        </div>
                      ) : null}
                    </TableRowCell>

                    <TableRowCell>
                      {file.quality ? <Label>{file.quality}</Label> : null}
                    </TableRowCell>

                    <TableRowCell>{formatBytes(file.size)}</TableRowCell>

                    <TableRowCell
                      className={styles.path}
                      title={file.localPath ?? file.path}
                    >
                      {file.path}
                    </TableRowCell>

                    <TableRowCell>
                      {file.missingSince ? (
                        <Label kind={kinds.WARNING}>
                          {translate('Missing')}
                        </Label>
                      ) : (
                        <Icon
                          name={file.readable ? icons.CHECK : icons.WARNING}
                          kind={file.readable ? kinds.SUCCESS : kinds.WARNING}
                          title={
                            file.readable
                              ? file.localPath
                              : `${translate('NotReadable')}: ${file.localPath}`
                          }
                        />
                      )}
                    </TableRowCell>

                    <RelativeDateCell date={file.dateAdded} />
                  </TableRow>
                ))}
              </TableBody>
            </Table>

            <TablePager
              page={page}
              totalPages={totalPages}
              totalRecords={totalRecords}
              isFetching={isFetching}
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
