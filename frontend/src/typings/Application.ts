import Provider from './Provider';

export enum ApplicationSyncLevel {
  Disabled = 'disabled',
  AddOnly = 'addOnly',
  FullSync = 'fullSync',
}

interface Application extends Provider {
  enable: boolean;
  syncLevel: ApplicationSyncLevel;
  tags: number[];
}

export default Application;
