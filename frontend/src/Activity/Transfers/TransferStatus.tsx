import React from 'react';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { getActivity } from './transferActivity';
import { useTransfers, useUploads } from './useTransfers';
import styles from './Transfers.css';

function TransferStatus() {
  const { data: transfers = [] } = useTransfers();
  const { data: uploads = [] } = useUploads(15000);
  const activity = getActivity(transfers, uploads);

  if (activity.receiving + activity.sending === 0) {
    return null;
  }

  const counts: [string, number][] = [
    ['ActivityDownloading', activity.downloading],
    ['ActivitySending', activity.uploading],
    ['ActivityWaiting', activity.waiting],
    ['ActivityRetrying', activity.retrying],
    ['ActivityFailed', activity.failed],
  ];
  const description = counts
    .filter(([, count]) => count > 0)
    .map(([key, count]) => translate(key, { count }))
    .join(', ');

  return (
    <Label
      className={
        activity.kind === 'warning' ? styles.warningStatus : styles.status
      }
      kind={activity.kind}
      size="medium"
      aria-label={description}
      title={description}
    >
      <span className={styles.counts} aria-hidden="true">
        {activity.receiving > 0 ? (
          <span className={styles.directionCount}>
            <Icon name={icons.DOWNLOAD} size={11} />
            {activity.receiving}
          </span>
        ) : null}
        {activity.sending > 0 ? (
          <span className={styles.directionCount}>
            <Icon name={icons.UPLOAD} size={11} />
            {activity.sending}
          </span>
        ) : null}
      </span>
    </Label>
  );
}

export default TransferStatus;
