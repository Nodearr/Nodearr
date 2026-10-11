import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import Health from './Health/Health';
import MoreInfo from './MoreInfo/MoreInfo';

function Status() {
  return (
    <PageContent title={translate('Status')}>
      <PageContentBody>
        <Health />

        {/* TODO: Add Diskspace and About components*/}
        <MoreInfo />
      </PageContentBody>
    </PageContent>
  );
}

export default Status;
