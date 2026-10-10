import React from 'react';
import { Route } from 'react-router-dom';
import Transfers from 'Activity/Transfers/Transfers';
import CatalogIndex from 'Catalog/Index/CatalogIndex';
import LocalCatalog from 'Catalog/Local/LocalCatalog';
import PeerCatalog from 'Catalog/Peer/PeerCatalog';
import Sharing from 'Catalog/Sharing/Sharing';
import NotFound from 'Components/NotFound';
import NodeInvites from 'Nodes/Invites/NodeInvites';
import NodesIndex from 'Nodes/NodesIndex/NodesIndex';
import TrustedNodes from 'Nodes/Trusted/TrustedNodes';
import Settings from 'Settings/Settings';
import Status from 'System/Status/Status';
import ApplicationSettings from '../Settings/Applications/ApplicationSettings';

export function appRouteElements() {
  return (
    <>
      {/*
        Catalog
      */}

      <Route path="/" element={<CatalogIndex />} />

      <Route path="/catalog/local" element={<LocalCatalog />} />

      <Route path="/catalog/peers" element={<PeerCatalog />} />

      <Route path="/catalog/sharing" element={<Sharing />} />

      {/*
        Nodes
      */}

      <Route path="/nodes" element={<NodesIndex />} />

      <Route path="/nodes/trusted" element={<TrustedNodes />} />

      <Route path="/nodes/invites" element={<NodeInvites />} />

      {/*
        Activity
      */}

      <Route path="/activity/transfers" element={<Transfers />} />

      {/*
        Settings
      */}

      <Route path="/settings" element={<Settings />} />

      <Route path="/settings/apps" element={<ApplicationSettings />} />

      <Route
        path="/settings/general"
        element={<div>Placeholder for general settings</div>}
      />

      {/*
        System
      */}

      <Route path="/system/status" element={<Status />} />

      {/*
        Not Found
      */}

      <Route path="*" element={<NotFound />} />
    </>
  );
}
