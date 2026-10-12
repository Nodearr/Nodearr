import React from 'react';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import SettingsToolbar from './SettingsToolbar';
import styles from './Settings.css';

function Settings() {
  return (
    <PageContent title={translate('Settings')}>
      <SettingsToolbar hasPendingChanges={false} />

      <PageContentBody>
        <Link className={styles.link} to="/settings/apps">
          {translate('Apps')}
        </Link>
        <div className={styles.summary}>{translate('AppSettingsSummary')}</div>

        <Link className={styles.link} to="/settings/node">
          {translate('NodeSettingsShort')}
        </Link>
        <div className={styles.summary}>{translate('NodeSettingsSummary')}</div>

        <Link className={styles.link} to="/settings/general">
          {translate('General')}
        </Link>
        <div className={styles.summary}>
          {translate('GeneralSettingsSummary')}
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default Settings;
