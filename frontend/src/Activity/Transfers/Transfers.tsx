import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
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
  getTransferKind,
  getUploadKind,
  isRetryingDownload,
  isWaitingForPeer,
} from './transferActivity';
import {
  Transfer,
  TransferAction,
  TransferState,
  Upload,
  UploadState,
  useTransferAction,
  useTransfers,
  useUploads,
} from './useTransfers';
import styles from './Transfers.css';

// The progress column's width less its padding. The bar draws its percentage across the whole
// bar only when it knows its width; without one the text is clipped to the part already done.
const PROGRESS_WIDTH = 144;

const COLUMNS: Column[] = [
  { name: 'title', label: () => translate('Title'), isVisible: true },
  { name: 'peerName', label: () => translate('Peer'), isVisible: true },
  { name: 'state', label: () => translate('Status'), isVisible: true },
  { name: 'progress', label: () => translate('Progress'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'added', label: () => translate('Added'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

const STATE_LABELS: Record<TransferState, string> = {
  queued: 'TransferQueued',
  downloading: 'TransferDownloading',
  completed: 'TransferCompleted',
  failed: 'TransferFailed',
};

const UPLOAD_COLUMNS: Column[] = [
  { name: 'title', label: () => translate('Title'), isVisible: true },
  { name: 'peerName', label: () => translate('UploadSentTo'), isVisible: true },
  { name: 'state', label: () => translate('Status'), isVisible: true },
  { name: 'progress', label: () => translate('Progress'), isVisible: true },
  { name: 'size', label: () => translate('Size'), isVisible: true },
  { name: 'started', label: () => translate('UploadStarted'), isVisible: true },
];

const UPLOAD_LABELS: Record<UploadState, string> = {
  waiting: 'UploadWaiting',
  preparing: 'UploadPreparing',
  sending: 'UploadSending',
  completed: 'UploadCompleted',
  failed: 'UploadFailed',
};

function UploadRow({ upload }: { upload: Upload }) {
  const { state, size, bytesSent } = upload;
  const percent = size > 0 ? Math.min(100, (bytesSent / size) * 100) : 0;
  const kind = getUploadKind(upload);

  return (
    <TableRow>
      <TableRowCell className={styles.title}>
        {upload.title}

        {state === 'waiting' && upload.queuePosition ? (
          <div className={styles.muted}>
            {translate('UploadWaitingInLine', {
              position: upload.queuePosition,
            })}
          </div>
        ) : null}

        {(state === 'preparing' || state === 'sending') &&
        upload.attempts > 1 &&
        upload.lastFailure ? (
          <div className={styles.muted}>
            {translate('UploadRetrying', {
              attempts: upload.attempts,
              failure: `${upload.lastFailure}${
                upload.lastFailureMessage
                  ? `: ${upload.lastFailureMessage}`
                  : ''
              }`,
            })}
          </div>
        ) : null}

        {state === 'failed' && upload.failure ? (
          <div className={styles.error}>
            {upload.failure}
            {upload.failureMessage ? `: ${upload.failureMessage}` : ''}
          </div>
        ) : null}
      </TableRowCell>

      <TableRowCell>{upload.peerName}</TableRowCell>

      <TableRowCell>
        <Label
          className={kind === 'warning' ? styles.warningStatus : styles.status}
          kind={kind}
        >
          {state === 'completed' && upload.attempts > 1
            ? translate('UploadCompletedAfterAttempts', {
                attempts: upload.attempts,
              })
            : translate(UPLOAD_LABELS[state])}
        </Label>
      </TableRowCell>

      <TableRowCell className={styles.progress}>
        <ProgressBar
          progress={state === 'completed' ? 100 : percent}
          kind={kind === 'info' ? 'primary' : kind}
          showText={true}
          width={PROGRESS_WIDTH}
          title={`${formatBytes(bytesSent)} / ${formatBytes(size)}`}
        />
      </TableRowCell>

      <TableRowCell>{formatBytes(size)}</TableRowCell>

      <RelativeDateCell date={upload.started} />
    </TableRow>
  );
}

interface TransferRowProps {
  transfer: Transfer;
  isBusy: boolean;
  onAction: (downloadId: string, action: TransferAction) => void;
}

function TransferRow({ transfer, isBusy, onAction }: TransferRowProps) {
  const { downloadId, state, size, bytesDone } = transfer;
  const percent = size > 0 ? Math.min(100, (bytesDone / size) * 100) : 0;
  const kind = getTransferKind(transfer);

  const handleRemovePress = useCallback(() => {
    onAction(downloadId, 'remove');
  }, [downloadId, onAction]);

  const handleRetryPress = useCallback(() => {
    onAction(downloadId, 'retry');
  }, [downloadId, onAction]);

  // A queued transfer that has already been tried is waiting to be tried again.
  const isWaitingToRetry = isRetryingDownload(transfer);

  // Downloading, but the friend's node has no free upload slot yet.
  const waitingForPeer = isWaitingForPeer(transfer);

  let label = STATE_LABELS[state];

  if (isWaitingToRetry) {
    label = 'TransferRetrying';
  } else if (waitingForPeer) {
    label = 'TransferWaiting';
  }

  return (
    <TableRow>
      <TableRowCell className={styles.title}>
        {transfer.title}

        {state === 'failed' || isWaitingToRetry ? (
          <div className={state === 'failed' ? styles.error : styles.muted}>
            {transfer.failureMessage}
          </div>
        ) : null}

        {waitingForPeer ? (
          <div className={styles.muted}>
            {translate('TransferWaitingForPeer', {
              peer: transfer.peerName ?? '',
              position: transfer.waitingPosition ?? 1,
            })}
          </div>
        ) : null}
      </TableRowCell>

      <TableRowCell>{transfer.peerName}</TableRowCell>

      <TableRowCell>
        <Label
          className={kind === 'warning' ? styles.warningStatus : styles.status}
          kind={kind}
        >
          {translate(label)}
        </Label>
      </TableRowCell>

      <TableRowCell className={styles.progress}>
        <ProgressBar
          progress={state === 'completed' ? 100 : percent}
          kind={kind === 'info' ? 'primary' : kind}
          showText={true}
          width={PROGRESS_WIDTH}
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
  const { data: transfers = [], isLoading, error } = useTransfers(15000);
  const { data: uploads = [], error: uploadsError } = useUploads();
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

        <FieldSet legend={translate('TransfersReceiving')}>
          {error ? (
            <Alert kind={kinds.DANGER}>{translate('TransfersLoadError')}</Alert>
          ) : null}

          {actionError ? (
            <Alert kind={kinds.DANGER}>{getErrorMessage(actionError)}</Alert>
          ) : null}

          {!isLoading && !error && transfers.length === 0 ? (
            <div className={styles.muted}>{translate('TransfersEmpty')}</div>
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
        </FieldSet>

        <FieldSet legend={translate('TransfersSending')}>
          {uploadsError ? (
            <Alert kind={kinds.DANGER}>{translate('UploadsLoadError')}</Alert>
          ) : null}

          {!uploadsError && uploads.length === 0 ? (
            <div className={styles.muted}>{translate('UploadsEmpty')}</div>
          ) : null}

          {uploads.length > 0 ? (
            <Table columns={UPLOAD_COLUMNS} canModifyColumns={false}>
              <TableBody>
                {uploads.map((upload) => (
                  <UploadRow key={upload.id} upload={upload} />
                ))}
              </TableBody>
            </Table>
          ) : null}
        </FieldSet>
      </PageContentBody>
    </PageContent>
  );
}

export default Transfers;
