import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import EditApplicationModal from 'Settings/Applications/Applications/EditApplicationModal';
import {
  fetchApplicationSchema,
  selectApplicationSchema,
} from 'Store/Actions/settingsActions';
import Application from 'typings/Application';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './Home.css';

// The apps whose libraries Nodearr reads, by implementation, and what each is called.
const APPS = [
  { implementation: 'Sonarr', label: 'Sonarr' },
  { implementation: 'Radarr', label: 'Radarr' },
  { implementation: 'WhisparrV3', label: 'Whisparr v3' },
  { implementation: 'WhisparrV2', label: 'Whisparr v2' },
];

interface ConnectButtonProps {
  app: string;
  label: string;
  isPrimary: boolean;
  isDisabled: boolean;
  onPress: (app: string) => void;
}

function ConnectButton({
  app,
  label,
  isPrimary,
  isDisabled,
  onPress,
}: ConnectButtonProps) {
  const handlePress = useCallback(() => {
    onPress(app);
  }, [app, onPress]);

  return (
    <Button
      kind={isPrimary ? kinds.PRIMARY : kinds.DEFAULT}
      isDisabled={isDisabled}
      onPress={handlePress}
    >
      {translate('HomeConnectApp', { app: label })}
    </Button>
  );
}

interface ConnectAppsStepProps {
  number: number;
  isActive: boolean;
  applications: Application[];
  onApplicationsChanged: () => void;
}

function ConnectAppsStep({
  number,
  isActive,
  applications,
  onApplicationsChanged,
}: ConnectAppsStepProps) {
  const dispatch = useDispatch();
  const [isModalOpen, setIsModalOpen] = useState(false);

  const { isSchemaPopulated } = useSelector(
    (state: AppState) => state.settings.applications
  );

  useEffect(() => {
    dispatch(fetchApplicationSchema());
  }, [dispatch]);

  const handleConnectPress = useCallback(
    (implementation: string) => {
      // The same form as Settings > Apps, opened on the app that was chosen.
      dispatch(selectApplicationSchema({ implementation }));
      setIsModalOpen(true);
    },
    [dispatch]
  );

  const handleModalClose = useCallback(() => {
    setIsModalOpen(false);
    onApplicationsChanged();
  }, [onApplicationsChanged]);

  const isDone = applications.length > 0;
  const connected = new Set(applications.map((app) => app.implementation));

  const connectButtons = APPS.filter(
    (app) => !connected.has(app.implementation)
  ).map((app) => (
    <ConnectButton
      key={app.implementation}
      app={app.implementation}
      label={app.label}
      isPrimary={!isDone}
      isDisabled={!isSchemaPopulated}
      onPress={handleConnectPress}
    />
  ));

  return (
    <>
      <SetupStep
        number={number}
        title={translate('HomeStepAppsTitle')}
        isDone={isDone}
        isActive={isActive}
        summary={translate('HomeStepAppsDone', {
          names: applications.map((app) => app.name).join(', '),
        })}
        doneActions={
          connectButtons.length ? (
            <div className={styles.buttons}>{connectButtons}</div>
          ) : null
        }
      >
        <p className={styles.stepText}>{translate('HomeStepAppsText')}</p>

        <div className={styles.buttons}>{connectButtons}</div>

        <p className={styles.hint}>{translate('HomeStepAppsHint')}</p>
      </SetupStep>

      <EditApplicationModal
        isOpen={isModalOpen}
        onModalClose={handleModalClose}
      />
    </>
  );
}

export default ConnectAppsStep;
