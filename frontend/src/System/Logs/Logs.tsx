import React from 'react';
import { Route } from 'react-router-dom';
import Switch from 'Components/Router/Switch';
import AppLogFiles from './App/AppLogFiles';

function Logs() {
  return (
    <Switch>
      <Route index={true} element={<AppLogFiles />} />
    </Switch>
  );
}

export default Logs;
