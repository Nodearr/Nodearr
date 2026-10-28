import { Transfer, Upload } from './useTransfers';

export type ActivityKind = 'info' | 'warning' | 'success' | 'danger';

export function isWaitingForPeer(transfer: Transfer) {
  return (
    transfer.state === 'downloading' &&
    typeof transfer.waitingPosition === 'number'
  );
}

export function isRetryingDownload(transfer: Transfer) {
  return transfer.state === 'queued' && transfer.attempts > 0;
}

export function isActiveUpload(upload: Upload) {
  return ['waiting', 'preparing', 'sending'].includes(upload.state);
}

export function getTransferKind(transfer: Transfer): ActivityKind {
  switch (transfer.state) {
    case 'failed':
      return 'danger';
    case 'completed':
      return 'success';
    case 'queued':
      return 'warning';
    default:
      return isWaitingForPeer(transfer) ? 'warning' : 'info';
  }
}

export function getUploadKind(upload: Upload): ActivityKind {
  switch (upload.state) {
    case 'failed':
      return 'danger';
    case 'completed':
      return 'success';
    default:
      return upload.state === 'waiting' || upload.attempts > 1
        ? 'warning'
        : 'info';
  }
}

export function getActivity(transfers: Transfer[], uploads: Upload[]) {
  const receiving = transfers.filter((item) => item.state !== 'completed');
  const sending = uploads.filter(isActiveUpload);
  const failed = receiving.filter((item) => item.state === 'failed').length;
  const retrying =
    receiving.filter(isRetryingDownload).length +
    sending.filter((item) => item.state !== 'waiting' && item.attempts > 1)
      .length;
  const waiting =
    receiving.filter(
      (item) =>
        isWaitingForPeer(item) ||
        (item.state === 'queued' && !isRetryingDownload(item))
    ).length + sending.filter((item) => item.state === 'waiting').length;
  let kind: ActivityKind = 'info';

  if (failed > 0) {
    kind = 'danger';
  } else if (waiting + retrying > 0) {
    kind = 'warning';
  }

  return {
    receiving: receiving.length,
    sending: sending.length,
    downloading: receiving.filter(
      (item) => item.state === 'downloading' && !isWaitingForPeer(item)
    ).length,
    uploading: sending.filter(
      (item) => item.state !== 'waiting' && item.attempts <= 1
    ).length,
    waiting,
    retrying,
    failed,
    kind,
  } as const;
}
