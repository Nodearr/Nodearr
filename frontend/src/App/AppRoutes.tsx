import React from 'react';
import { Route } from 'react-router-dom';
import CatalogIndex from "Catalog/Index/CatalogIndex";
import LocalCatalog from "Catalog/Local/LocalCatalog";
import NodeInvites from "Nodes/Invites/NodeInvites";
import NodesIndex from "Nodes/NodesIndex/NodesIndex";
import NotFound from "Components/NotFound";
import PeerCatalog from "Catalog/Peer/PeerCatalog";
import Transfers from "Activity/Transfers/Transfers";
import TrustedNodes from "Nodes/Trusted/TrustedNodes";
import Settings from "Settings/Settings";
import Status from 'System/Status/Status';

export function appRouteElements() {
  return (
    <>
      {/*
        Catalog
      */}

      <Route path={"/"} element={<CatalogIndex />} />

      <Route path={"/catalog/local"} element={<LocalCatalog />} />

      <Route path={"/catalog/peers"} element={<PeerCatalog />} />

      {/*
        Nodes
      */}

      <Route path={"/nodes"} element={<NodesIndex />} />

      <Route path={"/nodes/trusted"} element={<TrustedNodes />} />

      <Route path={"/nodes/invites"} element={<NodeInvites />} />

      {/*
        Activity
      */}

      <Route path="/activity/transfers" element={<Transfers />} />

      {/*
        Settings
      */}

      <Route path="/settings" element={<Settings />} />

      <Route path="/settings/apps" element={<div>Placeholder for apps</div>} />

      <Route path="/settings/general" element={<div>Placeholder for general settings</div>} />

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
