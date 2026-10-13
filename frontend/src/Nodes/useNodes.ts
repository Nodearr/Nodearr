import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export type PeerState = 'pending' | 'trusted' | 'blocked' | 'revoked';

export type ConnectionPath = 'unknown' | 'direct' | 'relay';

export interface NodeIdentity {
  transportRunning: boolean;
  nodeId?: string;
  name?: string;
  relayUrl?: string;
  ipAddresses: string[];
  // What the transport has worked out about this node's network. Absent until known.
  udp?: boolean;
  mappingVaries?: boolean;
  publicAddress?: string;
  captivePortal?: boolean;
  // In a container on Docker Desktop, where a varying public port is Docker's doing.
  dockerDesktop?: boolean;
}

export interface Peer {
  id: number;
  nodeId: string;
  name: string;
  state: PeerState;
  stateReason?: string;
  added: string;
  stateChanged: string;
  lastSeen?: string;
  lastError?: string;
  connectionPath: ConnectionPath;
  connectionAddress?: string;
  connectionChecked?: string;
  shareAll: boolean;
}

export interface Invite {
  id: number;
  label?: string;
  created: string;
  expires: string;
  redeemedAt?: string;
  redeemedByNodeId?: string;
  shareAll: boolean;
  code?: string;
}

const IDENTITY_PATH = '/nodes/identity';
const PEERS_PATH = '/nodes/peers';
const INVITES_PATH = '/nodes/invites';

function send<T>(method: 'POST' | 'DELETE', path: string, body?: unknown) {
  return fetchJson<T, unknown>({
    path: getQueryPath(path),
    method,
    body,
    headers: {
      'X-Api-Key': window.Nodearr.apiKey,
      'X-Nodearr-Client': 'Nodearr',
    },
  });
}

export function getErrorMessage(error: unknown) {
  if (error instanceof ApiError) {
    return error.statusBody?.message ?? error.message;
  }

  return error ? String(error) : undefined;
}

export const useNodeIdentity = () =>
  useApiQuery<NodeIdentity>({ path: IDENTITY_PATH });

export const usePeers = () => useApiQuery<Peer[]>({ path: PEERS_PATH });

export const useInvites = () => useApiQuery<Invite[]>({ path: INVITES_PATH });

export type PeerAction = 'ping' | 'block' | 'unblock' | 'revoke' | 'delete';

export const usePeerAction = () => {
  const queryClient = useQueryClient();

  return useMutation<unknown, ApiError, { id: number; action: PeerAction }>({
    mutationFn: ({ id, action }) =>
      action === 'delete'
        ? send('DELETE', `${PEERS_PATH}/${id}`)
        : send('POST', `${PEERS_PATH}/${id}/${action}`),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [PEERS_PATH] });
    },
  });
};

export const useCreateInvite = () => {
  const queryClient = useQueryClient();

  return useMutation<
    Invite,
    ApiError,
    { validHours: number; label: string; shareAll: boolean }
  >({
    mutationFn: (body) => send<Invite>('POST', INVITES_PATH, body),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [INVITES_PATH] });
    },
  });
};

export const useDeleteInvite = () => {
  const queryClient = useQueryClient();

  return useMutation<unknown, ApiError, number>({
    mutationFn: (id) => send('DELETE', `${INVITES_PATH}/${id}`),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [INVITES_PATH] });
    },
  });
};

export const useRedeemInvite = () => {
  const queryClient = useQueryClient();

  return useMutation<Peer, ApiError, string>({
    mutationFn: (code) =>
      send<Peer>('POST', `${INVITES_PATH}/redeem`, { code }),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: [PEERS_PATH] });
    },
  });
};
