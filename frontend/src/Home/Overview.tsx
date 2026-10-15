import React from 'react';
import { usePeerCatalogSummaries } from 'Catalog/useSharing';
import Icon, { IconKind } from 'Components/Icon';
import Link from 'Components/Link/Link';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, kinds } from 'Helpers/Props';
import { NodeIdentity, Peer } from 'Nodes/useNodes';
import useHealth from 'System/Status/Health/useHealth';
import Application from 'typings/Application';
import { LocalCatalogSummary } from 'typings/LocalCatalog';
import translate from 'Utilities/String/translate';
import styles from './Home.css';

interface OverviewProps {
  identity?: NodeIdentity;
  trusted: Peer[];
  applications: Application[];
}

export function networkSummary(identity?: NodeIdentity): {
  kind: IconKind;
  text: string;
} {
  if (!identity?.transportRunning) {
    return { kind: kinds.DANGER, text: translate('HomeNetworkNotRunning') };
  }

  if (identity.udp === false) {
    return { kind: kinds.WARNING, text: translate('HomeNetworkNoUdp') };
  }

  if (identity.mappingVaries && identity.dockerDesktop) {
    return {
      kind: kinds.WARNING,
      text: translate('HomeNetworkDockerDesktop'),
    };
  }

  if (identity.mappingVaries) {
    return { kind: kinds.WARNING, text: translate('HomeNetworkVaries') };
  }

  if (identity.udp) {
    return { kind: kinds.SUCCESS, text: translate('HomeNetworkGood') };
  }

  return { kind: kinds.INFO, text: translate('HomeNetworkChecking') };
}

function Stat({ value, label }: { value: number; label: string }) {
  return (
    <div>
      <div className={styles.statValue}>{value.toLocaleString()}</div>
      <div className={styles.statLabel}>{label}</div>
    </div>
  );
}

function connectionLabel(peer: Peer) {
  if (peer.connectionPath === 'direct') {
    return (
      <span className={styles.direct}>{translate('PeerConnectionDirect')}</span>
    );
  }

  if (peer.connectionPath === 'relay') {
    return (
      <span className={styles.relay}>{translate('PeerConnectionRelay')}</span>
    );
  }

  return (
    <span className={styles.muted}>
      {translate('PeerConnectionNotChecked')}
    </span>
  );
}

function Overview({ identity, trusted, applications }: OverviewProps) {
  const { data: health } = useHealth();
  const { data: peerSummaries = [] } = usePeerCatalogSummaries();
  const { data: library = [] } = useApiQuery<LocalCatalogSummary[]>({
    path: '/catalog/local',
  });

  const missing = peerSummaries.reduce((sum, peer) => sum + peer.missing, 0);
  const upgrades = peerSummaries.reduce((sum, peer) => sum + peer.upgrade, 0);
  const movies = library.reduce((sum, app) => sum + app.movieFiles, 0);
  const episodes = library.reduce((sum, app) => sum + app.episodeFiles, 0);
  const network = networkSummary(identity);
  const problems = health.filter((item) => item.type !== 'ok');

  return (
    <div className={styles.cards}>
      {problems.length ? (
        <div className={styles.cardWide}>
          <h2 className={styles.cardTitle}>
            <Icon name={icons.WARNING} kind={kinds.WARNING} />
            {translate('HomeNeedsAttention')}
          </h2>
          {problems.map((item) => (
            <p key={item.source + item.message} className={styles.attention}>
              {item.message}
            </p>
          ))}
          <Link className={styles.cardLink} to="/system/status">
            {translate('HomeSeeStatus')}
          </Link>
        </div>
      ) : null}

      <div className={styles.card}>
        <h2 className={styles.cardTitle}>
          <Icon name={icons.NODES} />
          {translate('HomeFromFriends')}
        </h2>
        <div className={styles.stats}>
          <Stat value={missing} label={translate('HomeMissingLabel')} />
          <Stat value={upgrades} label={translate('HomeUpgradeLabel')} />
        </div>
        {missing + upgrades === 0 ? (
          <p className={styles.muted}>{translate('HomeFromFriendsEmpty')}</p>
        ) : null}
        <Link className={styles.cardLink} to="/catalog/peers">
          {translate('HomeBrowseFriends')}
        </Link>
      </div>

      <div className={styles.card}>
        <h2 className={styles.cardTitle}>
          <Icon name={icons.CATALOG} />
          {translate('HomeYourLibrary')}
        </h2>
        <div className={styles.stats}>
          <Stat value={movies} label={translate('HomeMovieFilesLabel')} />
          <Stat value={episodes} label={translate('HomeEpisodeFilesLabel')} />
        </div>
        <div className={styles.muted}>
          {translate('HomeLibraryFrom', {
            names: applications.map((app) => app.name).join(', '),
          })}
        </div>
        <Link className={styles.cardLink} to="/catalog/sharing">
          {translate('HomeChooseSharing')}
        </Link>
      </div>

      <div className={styles.card}>
        <h2 className={styles.cardTitle}>
          <Icon name={icons.GROUP} />
          {translate('HomeFriends')}
        </h2>
        <ul className={styles.list}>
          {trusted.map((peer) => (
            <li key={peer.id} className={styles.listItem}>
              <span>{peer.name}</span>
              {connectionLabel(peer)}
            </li>
          ))}
        </ul>
        <Link className={styles.cardLink} to="/nodes/invites">
          {translate('HomeInviteAnother')}
        </Link>
      </div>

      <div className={styles.card}>
        <h2 className={styles.cardTitle}>
          <Icon name={icons.NETWORK} />
          {translate('HomeYourNode')}
        </h2>
        <p className={styles.attention}>
          <strong>{identity?.name}</strong>
        </p>
        <p className={styles.attention}>
          <Icon
            name={
              network.kind === kinds.SUCCESS ? icons.CHECK_CIRCLE : icons.INFO
            }
            kind={network.kind}
          />{' '}
          {network.text}
        </p>
        <Link className={styles.cardLink} to="/settings/node">
          {translate('HomeNodeSettings')}
        </Link>
      </div>
    </div>
  );
}

export default Overview;
