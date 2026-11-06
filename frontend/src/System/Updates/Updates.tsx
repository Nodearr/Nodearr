import React, { useCallback, useEffect } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { icons, kinds } from 'Helpers/Props';
import UpdateStatus, { Update } from 'typings/UpdateStatus';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import UpdateChanges from './UpdateChanges';
import useUpdates from './useUpdates';
import styles from './Updates.css';

function formatWhen(date?: string) {
  return date ? new Date(date).toLocaleString() : translate('Never');
}

interface InstallProps {
  status: UpdateStatus;
  update: Update;
  isInstalling: boolean;
  onInstallPress: () => void;
}

// What this install can do with the new version (ADR 0024).
function Install({
  status,
  update,
  isInstalling,
  onInstallPress,
}: InstallProps) {
  switch (status.installKind) {
    case 'builtIn':
    case 'script':
      return (
        <div>
          <p>
            {status.updateAutomatically
              ? translate('UpdateInstallsAutomatically')
              : translate('UpdateInstallsWhenAsked')}
          </p>

          {status.updateAutomatically && status.waitingSince ? (
            <p>
              {translate('UpdateWaitingSince', {
                time: formatWhen(status.waitingSince),
              })}
            </p>
          ) : null}

          <SpinnerButton
            kind={kinds.PRIMARY}
            isSpinning={isInstalling}
            onPress={onInstallPress}
          >
            {translate('InstallNow')}
          </SpinnerButton>
        </div>
      );

    case 'docker':
      return (
        <div>
          <p>{translate('UpdateDocker')}</p>

          {update.dockerCommand ? (
            <div className={styles.command}>
              <code>{update.dockerCommand}</code>
              <ClipboardButton value={update.dockerCommand} />
            </div>
          ) : null}
        </div>
      );

    case 'external':
      return <p>{translate('UpdateExternal')}</p>;

    case 'development':
      return <p>{translate('UpdateDevelopment')}</p>;

    default:
      return (
        <div>
          <p>
            {status.installKind === 'notWritable'
              ? translate('UpdateNotWritable')
              : translate('UpdateNoPackage')}
          </p>

          <Button to={update.releaseUrl}>{translate('UpdateDownload')}</Button>
        </div>
      );
  }
}

function Updates() {
  const executeCommand = useExecuteCommand();
  const { data: status, isFetching, isFetched, error, refetch } = useUpdates();

  const isChecking = useCommandExecuting(CommandNames.ApplicationUpdateCheck);
  const isInstalling = useCommandExecuting(CommandNames.ApplicationUpdate);
  const wasChecking = usePrevious(isChecking);

  const handleCheckPress = useCallback(() => {
    executeCommand({ name: CommandNames.ApplicationUpdateCheck });
  }, [executeCommand]);

  const handleInstallPress = useCallback(() => {
    executeCommand({ name: CommandNames.ApplicationUpdate });
  }, [executeCommand]);

  useEffect(() => {
    if (wasChecking && !isChecking) {
      refetch();
    }
  }, [isChecking, wasChecking, refetch]);

  const available = status?.available;

  return (
    <PageContent title={translate('Updates')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('CheckForUpdatesNow')}
            iconName={icons.REFRESH}
            isSpinning={isChecking}
            isDisabled={!status?.contactNodearr || !status?.updateCheck}
            onPress={handleCheckPress}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        {isFetching && !isFetched ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>{translate('UpdatesLoadError')}</Alert>
        ) : null}

        {status ? (
          <>
            {status.contactNodearr ? null : (
              <Alert kind={kinds.INFO}>
                {translate('UpdateCheckOffContactNodearr')}
              </Alert>
            )}

            {status.contactNodearr && !status.updateCheck ? (
              <Alert kind={kinds.INFO}>{translate('UpdateCheckOff')}</Alert>
            ) : null}

            {status.outcome === 'badSignature' ? (
              <Alert kind={kinds.DANGER}>
                {translate('UpdateCheckSignatureMessage')}
              </Alert>
            ) : null}

            {status.outcome === 'unavailable' ? (
              <Alert kind={kinds.WARNING}>
                {translate('UpdateCheckUnavailable')}
              </Alert>
            ) : null}

            <FieldSet legend={translate('Installed')} helpTopic="updates">
              <div className={styles.facts}>
                <div>
                  {translate('UpdateVersionRunning', {
                    version: status.currentVersion,
                  })}
                </div>

                <div>
                  {translate('UpdateLastChecked', {
                    time: formatWhen(status.lastChecked),
                  })}
                </div>

                <div>
                  <Link to="/system/logs/files/update">
                    {translate('UpdaterLogFiles')}
                  </Link>
                </div>
              </div>

              {status.outcome === 'checked' && !available ? (
                <Alert kind={kinds.SUCCESS}>{translate('UpdateNewest')}</Alert>
              ) : null}
            </FieldSet>

            {available ? (
              <FieldSet
                legend={translate('UpdateAvailableVersion', {
                  version: available.version,
                })}
              >
                <div className={styles.facts}>
                  <div>
                    {translate('UpdateReleased', {
                      time: formatWhen(available.releaseDate),
                    })}
                  </div>

                  {available.fileName ? (
                    <div>
                      {available.fileName} ({formatBytes(available.size)})
                    </div>
                  ) : null}
                </div>

                {available.summary ? <p>{available.summary}</p> : null}

                {available.changes.map((section) => (
                  <UpdateChanges
                    key={section.heading}
                    title={section.heading}
                    changes={section.items}
                  />
                ))}

                {available.changelog ? (
                  <p>
                    <Link to={available.changelog}>
                      {translate('UpdateChangelog')}
                    </Link>
                  </p>
                ) : null}

                <Install
                  status={status}
                  update={available}
                  isInstalling={isInstalling}
                  onInstallPress={handleInstallPress}
                />
              </FieldSet>
            ) : null}
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default Updates;
