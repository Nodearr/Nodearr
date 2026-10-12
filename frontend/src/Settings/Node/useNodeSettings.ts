import { useManageSettings } from 'Settings/useSettings';

export type RelayMode = 'n0' | 'custom' | 'disabled';

export interface NodeSettingsModel {
  id: number;
  nodeName: string;
  relay: RelayMode;
  relayUrl: string;
  port: number;
  portMapping: boolean;
  directTimeoutSeconds: number;
  downloadFolder: string;
  uploadLimitKbps: number;

  // Told by the server, and ignored when sent back.
  fixedByEnvironment: string[];
  defaultNodeName: string;
  defaultDownloadFolder: string;
  transportRunning: boolean;
  transportRestarted: boolean;
}

const PATH = '/settings/node';

export const useManageNodeSettings = () => {
  return useManageSettings<NodeSettingsModel>(PATH);
};
