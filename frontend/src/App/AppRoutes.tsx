import React from 'react';
import { Navigate, Route } from 'react-router-dom';
import Transfers from 'Activity/Transfers/Transfers';
import LocalCatalog from 'Catalog/Local/LocalCatalog';
import PeerCatalog from 'Catalog/Peer/PeerCatalog';
import Sharing from 'Catalog/Sharing/Sharing';
import NotFound from 'Components/NotFound';
import Home from 'Home/Home';
import NodeInvites from 'Nodes/Invites/NodeInvites';
import TrustedNodes from 'Nodes/Trusted/TrustedNodes';
import Settings from 'Settings/Settings';
import Backups from 'System/Backup/Backups';
import LogsTable from 'System/Events/LogsTable';
import Logs from 'System/Logs/Logs';
import Status from 'System/Status/Status';
import Tasks from 'System/Tasks/Tasks';
import ApplicationSettings from '../Settings/Applications/ApplicationSettings';
import GeneralSettings from '../Settings/General/GeneralSettings';
import NodeSettings from '../Settings/Node/NodeSettings';

export function appRouteElements() {
  return (
    <>
      {/*
        Home: setting up, and then an overview
      */}

      <Route path="/" element={<Home />} />

      {/*
        Catalog
      */}

      <Route
        path="/catalog"
        element={<Navigate to="/catalog/local" replace={true} />}
      />

      <Route path="/catalog/local" element={<LocalCatalog />} />

      <Route path="/catalog/peers" element={<PeerCatalog />} />

      <Route path="/catalog/sharing" element={<Sharing />} />

      {/*
        Nodes
      */}

      <Route
        path="/nodes"
        element={<Navigate to="/nodes/trusted" replace={true} />}
      />

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

      <Route path="/settings/node" element={<NodeSettings />} />

      <Route path="/settings/general" element={<GeneralSettings />} />

      {/*
        System
      */}

      <Route path="/system/status" element={<Status />} />

      <Route path="/system/tasks" element={<Tasks />} />

      <Route path="/system/backup" element={<Backups />} />

      <Route path="/system/events" element={<LogsTable />} />

      <Route path="/system/logs/files/*" element={<Logs />} />

      {/*
        Not Found
      */}

      <Route path="*" element={<NotFound />} />
    </>
  );
}
