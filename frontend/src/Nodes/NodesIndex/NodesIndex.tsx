import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';

function NodesIndex() {
  return (
    <PageContent title={translate('Nodes')}>
      <PageContentBody>
        <div>Nodes Index</div>
      </PageContentBody>
    </PageContent>
  );
}

export default NodesIndex;
