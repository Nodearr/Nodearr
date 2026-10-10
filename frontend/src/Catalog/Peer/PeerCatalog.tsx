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
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { icons, kinds } from 'Helpers/Props';
import { SortDirection } from 'Helpers/Props/sortDirections';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import {
  PeerCatalogItem,
  PeerItemStatus,
  usePeerCatalogSummaries,
} from '../useSharing';
import styles from '../Catalog.css';

const PAGE_SIZE = 50;

const COLUMNS: Column[] = [
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
  if (item.mediaKind === 'movie') {
    return item.year ? `${item.title} (${item.year})` : item.title;
  }

  const episodes = item.episodeNumbers.map((n) => `E${pad(n)}`).join('');

  return `${item.title} - S${pad(item.seasonNumber)}${episodes}`;
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

function PeerCatalog() {
  const [status, setStatus] = useState<PeerItemStatus | undefined>(undefined);
  const [page, setPage] = useState(1);
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
  } = usePeerCatalogSummaries();

  const {
    records,
    totalPages,
    totalRecords,
    isFetching,
    isLoading,
    error,
    refetch,
  } = usePagedApiQuery<PeerCatalogItem>({
    path: '/catalog/peers/items',
    page,
    pageSize: PAGE_SIZE,
    sortKey,
    sortDirection,
    queryParams: status ? { status } : undefined,
  });

  const handleSyncPress = useCallback(() => {
    executeCommand({ name: CommandNames.PeerCatalogSync }, () => {
      refetchSummaries();
      refetch();
    });
  }, [executeCommand, refetchSummaries, refetch]);

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
    setPage((current) => Math.min(totalPages, current + 1));
  }, [totalPages]);

  const handleLastPagePress = useCallback(() => {
    setPage(totalPages);
  }, [totalPages]);

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
      </PageToolbar>

      <PageContentBody>
        {isSummaryLoading ? <LoadingIndicator /> : null}

        {summaryError || error ? (
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
                {records.map((item) => (
                  <TableRow key={item.id}>
                    <TableRowCell>{item.peerName}</TableRowCell>

                    <TableRowCell className={styles.title}>
                      {describe(item)}
                      {item.episodeTitles ? (
                        <div className={styles.muted}>{item.episodeTitles}</div>
                      ) : null}
                    </TableRowCell>

                    <TableRowCell>
                      <Label kind={STATUS_KINDS[item.localStatus]}>
                        {translate(STATUS_LABELS[item.localStatus])}
                      </Label>
                    </TableRowCell>

                    <TableRowCell>
                      {item.quality ? <Label>{item.quality}</Label> : null}
                    </TableRowCell>

                    <TableRowCell>{formatBytes(item.size)}</TableRowCell>

                    <TableRowCell className={styles.release}>
                      {item.releaseTitle}
                    </TableRowCell>
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

export default PeerCatalog;
