import React from 'react';
import PageContent from "Components/Page/PageContent";
import PageContentBody from "Components/Page/PageContentBody";
import translate from "Utilities/String/translate";

function Transfers() {
  return (
    <PageContent title={translate('Transfers')}>
      <PageContentBody>
        <div>Transfers</div>
      </PageContentBody>
    </PageContent>
  )
}

export default Transfers;
