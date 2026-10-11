import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import {
  ConnectionPath,
  getErrorMessage,
  NodeIdentity,
  Peer,
  PeerAction,
  PeerState,
  useNodeIdentity,
  usePeerAction,
  usePeers,
} from '../useNodes';
import styles from '../Nodes.css';

const COLUMNS: Column[] = [
  { name: 'name', label: () => translate('Name'), isVisible: true },
  { name: 'state', label: () => translate('Status'), isVisible: true },
  {
    name: 'connection',
    label: () => translate('PeerConnection'),
    isVisible: true,
  },
  { name: 'lastSeen', label: () => translate('PeerLastSeen'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

const STATE_KINDS: Record<
  PeerState,
  'info' | 'success' | 'danger' | 'warning'
> = {
  pending: 'info',
  trusted: 'success',
  blocked: 'danger',
  revoked: 'warning',
};

const STATE_LABELS: Record<PeerState, string> = {
  pending: 'PeerStatePending',
  trusted: 'PeerStateTrusted',
  blocked: 'PeerStateBlocked',
  revoked: 'PeerStateRevoked',
};

// What can be done to a node depends on where trust stands.
const ACTIONS: Record<PeerState, PeerAction[]> = {
  pending: ['block', 'delete'],
  trusted: ['ping', 'revoke', 'block'],
  blocked: ['unblock'],
  revoked: ['block', 'delete'],
};

const ACTION_LABELS: Record<PeerAction, string> = {
  ping: 'PeerPing',
  block: 'PeerBlock',
  unblock: 'PeerUnblock',
  revoke: 'PeerRevoke',
  delete: 'Delete',
};

const CONNECTION_KINDS: Record<
  ConnectionPath,
  'default' | 'success' | 'warning'
> = {
  unknown: 'default',
  direct: 'success',
  relay: 'warning',
};

const CONNECTION_LABELS: Record<ConnectionPath, string> = {
  unknown: 'PeerConnectionNotChecked',
  direct: 'PeerConnectionDirect',
  relay: 'PeerConnectionRelay',
};

// What this node's network means for reaching other nodes, in one sentence.
function describeNetwork(identity: NodeIdentity, hasDirectPeer: boolean) {
  // A direct connection to another node is proof, whatever else was worked out.
  if (hasDirectPeer) {
    return { text: translate('PeerNetworkProvenDirect'), isProblem: false };
  }

  if (identity.captivePortal) {
    return { text: translate('PeerNetworkCaptivePortal'), isProblem: true };
  }

  if (identity.udp === false) {
    return { text: translate('PeerNetworkNoUdp'), isProblem: true };
  }

  if (identity.mappingVaries) {
    return { text: translate('PeerNetworkVaryingPort'), isProblem: true };
  }

  if (identity.udp && identity.mappingVaries === false) {
    return { text: translate('PeerNetworkGood'), isProblem: false };
  }

  return { text: translate('PeerNetworkUnknown'), isProblem: false };
}

interface PeerRowProps {
  peer: Peer;
  isBusy: boolean;
  onAction: (id: number, action: PeerAction) => void;
}

function PeerActionButton({
  id,
  action,
  isBusy,
  onAction,
}: {
  id: number;
  action: PeerAction;
  isBusy: boolean;
  onAction: (id: number, action: PeerAction) => void;
}) {
  const handlePress = useCallback(() => {
    onAction(id, action);
  }, [id, action, onAction]);

  return (
    <Button
      className={styles.actionButton}
      kind={
        action === 'block' || action === 'delete' ? kinds.DANGER : kinds.DEFAULT
      }
      isDisabled={isBusy}
      onPress={handlePress}
    >
      {translate(ACTION_LABELS[action])}
    </Button>
  );
}

function PeerRow({ peer, isBusy, onAction }: PeerRowProps) {
  return (
    <TableRow>
      <TableRowCell>
        <div>{peer.name}</div>
        <div className={styles.nodeId}>{peer.nodeId}</div>
      </TableRowCell>

      <TableRowCell>
        <Label kind={STATE_KINDS[peer.state]}>
          {translate(STATE_LABELS[peer.state])}
        </Label>
        {peer.stateReason ? (
          <div className={styles.muted}>{peer.stateReason}</div>
        ) : null}
        {peer.state === 'trusted' ? (
          <div className={styles.muted}>
            {peer.shareAll
              ? translate('SharingEverything')
              : translate('SharingListsOnly')}
          </div>
        ) : null}
        {peer.lastError ? (
          <div className={styles.error}>{peer.lastError}</div>
        ) : null}
      </TableRowCell>

      <TableRowCell>
        {peer.state === 'trusted' ? (
          <>
            <Label
              kind={CONNECTION_KINDS[peer.connectionPath]}
              title={
                peer.connectionPath === 'direct'
                  ? translate('PeerConnectionDirectHelpText')
                  : undefined
              }
            >
              {translate(CONNECTION_LABELS[peer.connectionPath])}
            </Label>

            {peer.connectionAddress ? (
              <div className={styles.nodeId}>{peer.connectionAddress}</div>
            ) : null}

            {peer.connectionPath === 'relay' ? (
              <div className={styles.error}>
                {translate('PeerConnectionRelayHelpText')}
              </div>
            ) : null}
          </>
        ) : null}
      </TableRowCell>

      {peer.lastSeen ? (
        <RelativeDateCell date={peer.lastSeen} />
      ) : (
        <TableRowCell>{translate('PeerNever')}</TableRowCell>
      )}

      <TableRowCell className={styles.actions}>
        {ACTIONS[peer.state].map((action) => (
          <PeerActionButton
            key={action}
            id={peer.id}
            action={action}
            isBusy={isBusy}
            onAction={onAction}
          />
        ))}
      </TableRowCell>
    </TableRow>
  );
}

function TrustedNodes() {
  const { data: identity } = useNodeIdentity();
  const { data: peers = [], isLoading, error } = usePeers();
  const { mutate, isPending, error: actionError } = usePeerAction();

  const handleAction = useCallback(
    (id: number, action: PeerAction) => {
      mutate({ id, action });
    },
    [mutate]
  );

  const hasDirectPeer = peers.some(
    (peer) => peer.state === 'trusted' && peer.connectionPath === 'direct'
  );

  return (
    <PageContent title={translate('TrustedNodes')}>
      <PageContentBody>
        {identity ? (
          <div className={styles.identity}>
            <div className={styles.identityName}>
              {translate('NodeIdentity')}: {identity.name}
            </div>

            {identity.transportRunning ? (
              <>
                <div className={styles.muted}>{translate('NodeId')}</div>
                <div className={styles.nodeId}>{identity.nodeId}</div>

                <div className={styles.network}>
                  <div className={styles.muted}>{translate('PeerNetwork')}</div>
                  <div
                    className={
                      describeNetwork(identity, hasDirectPeer).isProblem
                        ? styles.error
                        : undefined
                    }
                  >
                    {describeNetwork(identity, hasDirectPeer).text}
                  </div>

                  {identity.publicAddress ? (
                    <div className={styles.muted}>
                      {translate('PeerNetworkPublicAddress')}:{' '}
                      <span className={styles.nodeId}>
                        {identity.publicAddress}
                      </span>
                    </div>
                  ) : null}

                  {identity.relayUrl ? (
                    <div className={styles.muted}>
                      {translate('PeerRelay')}:{' '}
                      <span className={styles.nodeId}>{identity.relayUrl}</span>
                    </div>
                  ) : null}
                </div>
              </>
            ) : (
              <Alert kind={kinds.DANGER}>
                {translate('NodeTransportNotRunning')}
              </Alert>
            )}
          </div>
        ) : null}

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('NodesLoadError')}</Alert>
        ) : null}

        {actionError ? (
          <Alert kind={kinds.DANGER}>{getErrorMessage(actionError)}</Alert>
        ) : null}

        {!isLoading && !error && peers.length === 0 ? (
          <Alert kind={kinds.INFO}>{translate('NoTrustedNodes')}</Alert>
        ) : null}

        {peers.length > 0 ? (
          <Table columns={COLUMNS} canModifyColumns={false}>
            <TableBody>
              {peers.map((peer) => (
                <PeerRow
                  key={peer.id}
                  peer={peer}
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

export default TrustedNodes;
