import React from 'react';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';

function CatalogIndex() {
  return (
    <PageContent title={translate('Catalog')}>
      <PageContentBody>
        <div>Catalog Index</div>
      </PageContentBody>
    </PageContent>
  );
}

export default CatalogIndex;
