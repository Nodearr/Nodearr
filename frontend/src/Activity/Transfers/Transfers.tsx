import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import ProgressBar from 'Components/ProgressBar';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import { getErrorMessage } from 'Nodes/useNodes';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import {
  Transfer,
  TransferAction,
  TransferState,
  useTransferAction,
  useTransfers,
} from './useTransfers';
import styles from './Transfers.css';

const COLUMNS: Column[] = [
  { name: 'title', label: () => translate('Title'), isVisible: true },
  { name: 'peerName', label: () => translate('Peer'), isVisible: true },
  { name: 'state', label: () => translate('Status'), isVisible: true },
  { name: 'progress', label: () => translate('Progress'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'added', label: () => translate('Added'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

const STATE_KINDS: Record<
  TransferState,
  'default' | 'info' | 'success' | 'danger'
> = {
  queued: 'default',
  downloading: 'info',
  completed: 'success',
  failed: 'danger',
};

const STATE_LABELS: Record<TransferState, string> = {
  queued: 'TransferQueued',
  downloading: 'TransferDownloading',
  completed: 'TransferCompleted',
  failed: 'TransferFailed',
};

interface TransferRowProps {
  transfer: Transfer;
  isBusy: boolean;
  onAction: (downloadId: string, action: TransferAction) => void;
}

function TransferRow({ transfer, isBusy, onAction }: TransferRowProps) {
  const { downloadId, state, size, bytesDone } = transfer;
  const percent = size > 0 ? Math.min(100, (bytesDone / size) * 100) : 0;

  const handleRemovePress = useCallback(() => {
    onAction(downloadId, 'remove');
  }, [downloadId, onAction]);

  const handleRetryPress = useCallback(() => {
    onAction(downloadId, 'retry');
  }, [downloadId, onAction]);

  // A queued transfer that has already been tried is waiting to be tried again.
  const isWaitingToRetry = state === 'queued' && transfer.attempts > 0;

  return (
    <TableRow>
      <TableRowCell className={styles.title}>
        {transfer.title}

        {state === 'failed' || isWaitingToRetry ? (
          <div className={state === 'failed' ? styles.error : styles.muted}>
            {transfer.failureMessage}
          </div>
        ) : null}
      </TableRowCell>

      <TableRowCell>{transfer.peerName}</TableRowCell>

      <TableRowCell>
        <Label kind={STATE_KINDS[state]}>
          {translate(
            isWaitingToRetry ? 'TransferRetrying' : STATE_LABELS[state]
          )}
        </Label>
      </TableRowCell>

      <TableRowCell className={styles.progress}>
        <ProgressBar
          progress={state === 'completed' ? 100 : percent}
          kind={state === 'failed' ? kinds.DANGER : kinds.PRIMARY}
          showText={true}
          title={`${formatBytes(bytesDone)} / ${formatBytes(size)}`}
        />
      </TableRowCell>

      <TableRowCell>{formatBytes(size)}</TableRowCell>

      <RelativeDateCell date={transfer.added} />

      <TableRowCell className={styles.actions}>
        {state === 'failed' ? (
          <Button
            className={styles.actionButton}
            isDisabled={isBusy}
            onPress={handleRetryPress}
          >
            {translate('Retry')}
          </Button>
        ) : null}

        <Button
          className={styles.actionButton}
          kind={kinds.DANGER}
          isDisabled={isBusy}
          onPress={handleRemovePress}
        >
          {translate('Remove')}
        </Button>
      </TableRowCell>
    </TableRow>
  );
}

function Transfers() {
  const { data: transfers = [], isLoading, error } = useTransfers();
  const { mutate, isPending, error: actionError } = useTransferAction();

  const handleAction = useCallback(
    (downloadId: string, action: TransferAction) => {
      mutate({ downloadId, action });
    },
    [mutate]
  );

  return (
    <PageContent title={translate('Transfers')}>
      <PageContentBody>
        <Alert kind={kinds.INFO}>{translate('TransfersHelpText')}</Alert>

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('TransfersLoadError')}</Alert>
        ) : null}

        {actionError ? (
          <Alert kind={kinds.DANGER}>{getErrorMessage(actionError)}</Alert>
        ) : null}

        {!isLoading && !error && transfers.length === 0 ? (
          <Alert kind={kinds.INFO}>{translate('TransfersEmpty')}</Alert>
        ) : null}

        {transfers.length > 0 ? (
          <Table columns={COLUMNS} canModifyColumns={false}>
            <TableBody>
              {transfers.map((transfer) => (
                <TransferRow
                  key={transfer.downloadId}
                  transfer={transfer}
                  isBusy={isPending}
                  onAction={handleAction}
                />
              ))}
            </TableBody>
          </Table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default Transfers;
