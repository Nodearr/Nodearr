import React from "react";
import PageContent from "Components/Page/PageContent";
import PageContentBody from "Components/Page/PageContentBody";
import translate from "Utilities/String/translate";

function PeerCatalog() {
  return (
    <PageContent title={translate('PeerCatalog')}>
      <PageContentBody>
        <div>Peer Catalog</div>
      </PageContentBody>
    </PageContent>
  )
}

export default PeerCatalog;
