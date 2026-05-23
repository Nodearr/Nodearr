import React from 'react';
import PageContent from "Components/Page/PageContent";
import PageContentBody from "Components/Page/PageContentBody";
import translate from "Utilities/String/translate";

function LocalCatalog() {
  return (
    <PageContent title={translate("Local")}>
      <PageContentBody>
        <div>Local Catalog</div>
      </PageContentBody>
    </PageContent>
  )
}

export default LocalCatalog;
