// Keep published guide destinations here so page and task help stay consistent.
export const documentation = {
  overview: { path: '/guide/', label: 'Documentation' },
  setup: { path: '/getting-started/', label: 'Home' },
  localCatalog: {
    path: '/guide/finding-and-fetching/#what-you-have',
    label: 'LocalCatalog',
  },
  peerCatalog: {
    path: '/guide/finding-and-fetching/#what-your-friends-have',
    label: 'PeerCatalog',
  },
  sharing: {
    path: '/guide/friends-and-sharing/#choosing-what-to-share',
    label: 'Sharing',
  },
  friends: {
    path: '/guide/friends-and-sharing/#your-friends',
    label: 'TrustedNodes',
  },
  invites: {
    path: '/guide/friends-and-sharing/#inviting-a-friend',
    label: 'Invites',
  },
  redeemInvite: {
    path: '/guide/friends-and-sharing/#using-a-friends-invite',
    label: 'InviteRedeem',
  },
  send: {
    path: '/guide/friends-and-sharing/#sending-something-to-a-friend',
    label: 'SendToFriend',
  },
  hashing: {
    path: '/guide/friends-and-sharing/#hashing',
    label: 'SharingHashing',
  },
  transfers: {
    path: '/guide/finding-and-fetching/#what-happens-to-a-grab',
    label: 'Transfers',
  },
  getIt: {
    path: '/guide/finding-and-fetching/#get-it-from-the-catalog',
    label: 'GetIt',
  },
  settings: { path: '/guide/settings/', label: 'Settings' },
  node: { path: '/guide/settings/#settings--node', label: 'NodeSettings' },
  general: { path: '/guide/settings/#settings--general', label: 'General' },
  host: { path: '/guide/settings/#host', label: 'Host' },
  security: { path: '/guide/settings/#security', label: 'Security' },
  logging: { path: '/guide/settings/#logging', label: 'Logging' },
  backups: { path: '/guide/backups/', label: 'Backups' },
  apps: { path: '/guide/connecting-apps/', label: 'Applications' },
  paths: {
    path: '/guide/connecting-apps/#the-two-folders-that-have-to-line-up',
    label: 'RemotePathMappings',
  },
  troubleshooting: {
    path: '/guide/troubleshooting/',
    label: 'Troubleshooting',
  },
} as const;

export type DocumentationTopic = keyof typeof documentation;

const pageTopics: Record<string, DocumentationTopic> = {
  '/': 'setup',
  '/catalog/local': 'localCatalog',
  '/catalog/peers': 'peerCatalog',
  '/catalog/sharing': 'sharing',
  '/nodes/trusted': 'friends',
  '/nodes/invites': 'invites',
  '/activity/transfers': 'transfers',
  '/settings': 'settings',
  '/settings/apps': 'apps',
  '/settings/node': 'node',
  '/settings/general': 'general',
  '/system/status': 'troubleshooting',
  '/system/tasks': 'troubleshooting',
  '/system/backup': 'backups',
  '/system/events': 'troubleshooting',
};

export function getPageDocumentation(pathname: string): DocumentationTopic {
  const path = pathname.replace(/\/+$/, '') || '/';

  if (path === '/system/logs/files' || path.startsWith('/system/logs/files/')) {
    return 'logging';
  }

  return pageTopics[path] ?? 'overview';
}
