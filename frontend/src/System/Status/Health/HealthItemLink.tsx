import React from 'react';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface HealthItemLinkProps {
  source: string;
}

// The page where what a check found is put right. Only checks with a page in Nodearr get a link:
// the checks inherited from Sonarr for its own indexers, download clients, notifications and root
// folders have none here, and a link to one led to the page for a page that does not exist.
function HealthItemLink(props: HealthItemLinkProps) {
  const { source } = props;

  switch (source) {
    case 'LibraryReadableCheck':
    case 'ApplicationDownloadFolderCheck':
      return (
        <IconButton
          name={icons.SETTINGS}
          title={translate('Settings')}
          aria-label={translate('Settings')}
          to="/settings/apps"
        />
      );
    case 'PeerTransportCheck':
    case 'PeerNetworkCheck':
      return (
        <IconButton
          name={icons.SETTINGS}
          title={translate('TrustedNodes')}
          aria-label={translate('TrustedNodes')}
          to="/nodes/trusted"
        />
      );
    default:
      return null;
  }
}

export default HealthItemLink;
