import React, { useCallback, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import { icons } from 'Helpers/Props';
import SettingsToolbar from 'Settings/SettingsToolbar';
import { testAllApplications } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import ApplicationsConnector from './Applications/ApplicationsConnector';
import ManageApplicationsModal from './Applications/Manage/ManageApplicationsModal';

function ApplicationSettings() {
  const executeCommand = useExecuteCommand();
  const isSyncingIndexers = useCommandExecuting(
    CommandNames.ApplicationIndexerSync
  );
  const isTestingAll = useSelector(
    (state: AppState) => state.settings.applications.isTestingAll
  );
  const dispatch = useDispatch();

  const [isManageApplicationsOpen, setIsManageApplicationsOpen] =
    useState(false);

  const onManageApplicationsPress = useCallback(() => {
    setIsManageApplicationsOpen(true);
  }, [setIsManageApplicationsOpen]);

  const onManageApplicationsModalClose = useCallback(() => {
    setIsManageApplicationsOpen(false);
  }, [setIsManageApplicationsOpen]);

  const onAppIndexerSyncPress = useCallback(() => {
    executeCommand({
      name: CommandNames.ApplicationIndexerSync,
      forceSync: true,
    });
  }, [executeCommand]);

  const onTestAllPress = useCallback(() => {
    dispatch(testAllApplications());
  }, [dispatch]);

  return (
    <PageContent title={translate('Applications')}>
      <SettingsToolbar
        showSave={false}
        additionalButtons={
          <>
            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('SyncAppIndexers')}
              iconName={icons.REFRESH}
              isSpinning={isSyncingIndexers}
              onPress={onAppIndexerSyncPress}
            />

            <PageToolbarButton
              label={translate('TestAllApps')}
              iconName={icons.TEST}
              isSpinning={isTestingAll}
              onPress={onTestAllPress}
            />

            <PageToolbarButton
              label={translate('ManageApplications')}
              iconName={icons.MANAGE}
              onPress={onManageApplicationsPress}
            />
          </>
        }
      />

      <PageContentBody>
        <ApplicationsConnector />

        <ManageApplicationsModal
          isOpen={isManageApplicationsOpen}
          onModalClose={onManageApplicationsModalClose}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default ApplicationSettings;
