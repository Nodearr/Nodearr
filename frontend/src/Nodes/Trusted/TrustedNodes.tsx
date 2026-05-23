import React from "react";
import PageContent from "Components/Page/PageContent";
import PageContentBody from "Components/Page/PageContentBody";
import translate from "Utilities/String/translate";

function TrustedNodes() {
  return (
    <PageContent title={translate('TrustedNodes')}>
      <PageContentBody>
        <div>Trusted Nodes</div>
      </PageContentBody>
    </PageContent>
  )
}

export default TrustedNodes;
