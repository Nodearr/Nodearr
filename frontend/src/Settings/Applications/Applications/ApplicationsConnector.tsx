import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { ApplicationAppState } from 'App/State/SettingsAppState';
import { fetchApplications } from 'Store/Actions/settingsActions';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import ApplicationModel from 'typings/Application';
import sortByProp from 'Utilities/Array/sortByProp';
import Applications from './Applications';

function ApplicationsConnector() {
  const dispatch = useDispatch();

  const { error, isFetching, isPopulated, items } = useSelector(
    createSortedSectionSelector<ApplicationModel, ApplicationAppState>(
      'settings.applications',
      sortByProp('name')
    )
  );

  useEffect(() => {
    dispatch(fetchApplications());
  }, [dispatch]);

  return (
    <Applications
      error={error}
      isFetching={isFetching}
      isPopulated={isPopulated}
      items={items}
    />
  );
}

export default ApplicationsConnector;
