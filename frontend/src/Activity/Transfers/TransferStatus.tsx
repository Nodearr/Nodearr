import React from 'react';
import PageSidebarStatus from 'Components/Page/Sidebar/PageSidebarStatus';
import translate from 'Utilities/String/translate';
import { useTransfers } from './useTransfers';

function TransferStatus() {
  const { data: transfers = [] } = useTransfers();
  // A failed download remains unresolved until it is retried or removed.
  const count = transfers.filter(
    (transfer) => transfer.state !== 'completed'
  ).length;
  const failed = transfers.filter(
    (transfer) => transfer.state === 'failed'
  ).length;

  const description = failed
    ? translate(
        count === 1 ? 'ActivityDownloadFailedOne' : 'ActivityDownloadsFailed',
        { count, failed }
      )
    : translate(count === 1 ? 'ActivityDownloadOne' : 'ActivityDownloads', {
        count,
      });

  return (
    <PageSidebarStatus
      count={count}
      errors={failed > 0}
      aria-label={description}
      title={description}
    />
  );
}

export default TransferStatus;
