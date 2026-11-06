export type UpdateCheckOutcome =
  | 'notChecked'
  | 'off'
  | 'checked'
  | 'unavailable'
  | 'badSignature';

export type UpdateInstallKind =
  | 'builtIn'
  | 'script'
  | 'docker'
  | 'external'
  | 'notWritable'
  | 'noPackage'
  | 'development';

export interface UpdateChanges {
  heading: string;
  items: string[];
}

export interface Update {
  version: string;
  releaseDate: string;
  changelog?: string;
  summary?: string;
  changes: UpdateChanges[];
  fileName?: string;
  url?: string;
  size: number;
  releaseUrl: string;
  dockerCommand?: string;
}

interface UpdateStatus {
  currentVersion: string;
  branch: string;
  updateCheck: boolean;
  contactNodearr: boolean;
  updateAutomatically: boolean;
  outcome: UpdateCheckOutcome;
  lastChecked?: string;
  available?: Update;
  availableSince?: string;
  installKind: UpdateInstallKind;
  waitingSince?: string;
}

export default UpdateStatus;
