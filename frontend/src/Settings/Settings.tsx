import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';

function Settings() {
  return (
    <PageContent title={translate('Settings')}>
      <PageContentBody>
        Settings
      </PageContentBody>
    </PageContent>
  );
}

export default Settings;
