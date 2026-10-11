import React from 'react';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface HealthItemLinkProps {
  source: string;
}

function HealthItemLink(props: HealthItemLinkProps) {
  const { source } = props;

  switch (source) {
    case 'IndexerRssCheck':
    case 'IndexerSearchCheck':
    case 'IndexerStatusCheck':
    case 'IndexerJackettAllCheck':
    case 'IndexerLongTermStatusCheck':
      return (
        <IconButton
          name={icons.SETTINGS}
          title={translate('Settings')}
          aria-label={translate('Settings')}
          to="/settings/indexers"
        />
      );
    case 'DownloadClientCheck':
    case 'DownloadClientStatusCheck':
    case 'ImportMechanismCheck':
      return (
        <IconButton
          name={icons.SETTINGS}
          title={translate('Settings')}
          aria-label={translate('Settings')}
          to="/settings/downloadclients"
        />
      );
    case 'NotificationStatusCheck':
      return (
        <IconButton
          name={icons.SETTINGS}
          title={translate('Settings')}
          aria-label={translate('Settings')}
          to="/settings/connect"
        />
      );
    case 'RootFolderCheck':
      return (
        <IconButton
          name={icons.CATALOG}
          title={translate('SeriesEditor')}
          aria-label={translate('SeriesEditor')}
          to="/serieseditor"
        />
      );
    case 'LibraryReadableCheck':
    case 'ApplicationDownloadFolderCheck':
      return (
        <IconButton
          name={icons.SETTINGS}
          title={translate('Settings')}
          aria-label={translate('Settings')}
          to="/settings/applications"
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
