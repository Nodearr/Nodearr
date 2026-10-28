import React from 'react';
import translate from 'Utilities/String/translate';
import { PeerItemStatus } from './useSharing';
import styles from './CatalogAvailability.css';

export const STATUS_LABELS: Record<PeerItemStatus, string> = {
  have: 'PeerItemHave',
  missing: 'PeerItemMissing',
  upgrade: 'PeerItemUpgrade',
  notInLibrary: 'PeerItemNotInLibrary',
};

function CatalogAvailability({
  status,
  children,
}: {
  status: PeerItemStatus;
  children?: React.ReactNode;
}) {
  return (
    <span
      className={`${styles.status} ${styles[status]}`}
      data-catalog-status={status}
      title={
        status === 'upgrade' ? translate('PeerItemUpgradeHelpText') : undefined
      }
    >
      {children ?? translate(STATUS_LABELS[status])}
    </span>
  );
}

export default CatalogAvailability;
