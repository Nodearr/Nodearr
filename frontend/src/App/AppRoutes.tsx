import React from 'react';
import { Route } from 'react-router-dom';
import Status from 'System/Status/Status';
import NotFound from "Components/NotFound";

export function appRouteElements() {
  return (
    <>
      {/*
        Catalog
      */}

      <Route path={"/"} element={<div>Placeholder for catalog</div>} />

      <Route path={"/catalog/local"} element={<div>Placeholder for local catalog</div>} />

      <Route path={"/catalog/peers"} element={<div>Placeholder for peers catalog</div>} />

      {/*
        Nodes
      */}

      <Route path={"/nodes"} element={<div>Placeholder for nodes</div>} />

      <Route path={"/nodes/trusted"} element={<div>Placeholder for trusted nodes</div>} />

      <Route path={"/nodes/invites"} element={<div>Placeholder for invites</div>} />

      {/*
        Activity
      */}

      <Route path="/activity/transfers" element={<div>Placeholder for transfers</div>} />

      {/*
        Settings
      */}

      <Route path="/settings" element={<div>Placeholder for settings</div>} />

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
