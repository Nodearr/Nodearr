import React, { useCallback, useEffect, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds } from 'Helpers/Props';
import SettingsToolbar from 'Settings/SettingsToolbar';
import { useIsWindowsService } from 'System/Status/useSystemStatus';
import { useRestart } from 'System/useSystem';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import BackupSettings from './BackupSettings';
import HostSettings from './HostSettings';
import LoggingSettings from './LoggingSettings';
import SecuritySettings from './SecuritySettings';
import { useManageGeneralSettings } from './useGeneralSettings';

const requiresRestartKeys = [
  'bindAddress',
  'port',
  'urlBase',
  'instanceName',
  'enableSsl',
  'sslPort',
  'sslCertHash',
  'sslCertPassword',
];

function GeneralSettings() {
  const isWindowsService = useIsWindowsService();
  const { mutate: restart } = useRestart();
  const isResettingApiKey = useCommandExecuting(CommandNames.ResetApiKey);

  const {
    settings,
    isFetching,
    isFetched,
    error,
    updateSetting,
    saveSettings,
    isSaving,
    saveError,
    hasPendingChanges,
    pendingChanges,
    validationErrors,
    validationWarnings,
  } = useManageGeneralSettings();

  const wasResettingApiKey = usePrevious(isResettingApiKey);
  const wasSaving = usePrevious(isSaving);
  const previousPendingChanges = usePrevious(pendingChanges);

  const [isRestartRequiredModalOpen, setIsRestartRequiredModalOpen] =
    useState(false);

  // Settings given in the environment Nodearr runs in. They are shown and cannot be changed.
  const fixedNames = (settings?.fixedByEnvironment?.value ?? []).map(
    (name: string) => name.toLowerCase()
  );

  const isFixed = useCallback(
    (name: string) => fixedNames.includes(name.toLowerCase()),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [fixedNames.join(',')]
  );

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error input change events aren't typed
      updateSetting(change.name, change.value);
    },
    [updateSetting]
  );

  const handleSavePress = useCallback(() => {
    saveSettings();
  }, [saveSettings]);

  const handleConfirmRestart = useCallback(() => {
    setIsRestartRequiredModalOpen(false);
    restart();
  }, [restart]);

  const handleCloseRestartRequiredModalOpen = useCallback(() => {
    setIsRestartRequiredModalOpen(false);
  }, []);

  useEffect(() => {
    const isRestartedRequired =
      previousPendingChanges &&
      Object.keys(previousPendingChanges).some((key) => {
        return requiresRestartKeys.includes(key);
      });

    if (!isSaving && wasSaving && !saveError && isRestartedRequired) {
      setIsRestartRequiredModalOpen(true);
    }
  }, [isSaving, wasSaving, saveError, previousPendingChanges]);

  // The page was loaded with the old key and can ask for nothing more with it. Loading it again
  // picks up the new one. No restart is needed: the key is checked afresh on every request.
  useEffect(() => {
    if (!isResettingApiKey && wasResettingApiKey) {
      window.location.reload();
    }
  }, [isResettingApiKey, wasResettingApiKey]);

  return (
    <PageContent title={translate('GeneralSettings')}>
      <SettingsToolbar
        hasPendingChanges={hasPendingChanges}
        isSaving={isSaving}
        onSavePress={handleSavePress}
      />

      <PageContentBody>
        {isFetching && !isFetched ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>
            {translate('GeneralSettingsLoadError')}
          </Alert>
        ) : null}

        {settings && isFetched && !error ? (
          <Form
            id="generalSettings"
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            {fixedNames.length > 0 ? (
              <Alert kind={kinds.INFO}>
                {translate('NodeSettingsSomeFixed')}
              </Alert>
            ) : null}

            <HostSettings
              bindAddress={settings.bindAddress}
              port={settings.port}
              urlBase={settings.urlBase}
              instanceName={settings.instanceName}
              applicationUrl={settings.applicationUrl}
              enableSsl={settings.enableSsl}
              sslPort={settings.sslPort}
              sslCertPath={settings.sslCertPath}
              sslKeyPath={settings.sslKeyPath}
              sslCertPassword={settings.sslCertPassword}
              launchBrowser={settings.launchBrowser}
              isFixed={isFixed}
              onInputChange={handleInputChange}
            />

            <SecuritySettings
              authenticationMethod={settings.authenticationMethod}
              authenticationRequired={settings.authenticationRequired}
              username={settings.username}
              password={settings.password}
              passwordConfirmation={settings.passwordConfirmation}
              apiKey={settings.apiKey}
              certificateValidation={settings.certificateValidation}
              isResettingApiKey={isResettingApiKey}
              isFixed={isFixed}
              onInputChange={handleInputChange}
            />

            <LoggingSettings
              logLevel={settings.logLevel}
              logSizeLimit={settings.logSizeLimit}
              isFixed={isFixed}
              onInputChange={handleInputChange}
            />

            <BackupSettings
              backupFolder={settings.backupFolder}
              backupInterval={settings.backupInterval}
              backupRetention={settings.backupRetention}
              onInputChange={handleInputChange}
            />
          </Form>
        ) : null}
      </PageContentBody>

      <ConfirmModal
        isOpen={isRestartRequiredModalOpen}
        kind={kinds.DANGER}
        title={translate('RestartNodearr')}
        message={`${translate('RestartRequiredToApplyChanges')} ${
          isWindowsService ? translate('RestartRequiredWindowsService') : ''
        }`}
        cancelLabel={translate('RestartLater')}
        confirmLabel={translate('RestartNow')}
        onConfirm={handleConfirmRestart}
        onCancel={handleCloseRestartRequiredModalOpen}
      />
    </PageContent>
  );
}

export default GeneralSettings;
