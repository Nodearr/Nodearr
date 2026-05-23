import React from 'react';
import PageContent from "Components/Page/PageContent";
import PageContentBody from "Components/Page/PageContentBody";
import translate from "Utilities/String/translate";

function NodeInvites() {
  return (
    <PageContent title={translate('Invites')}>
      <PageContentBody>
        <div>Node Invites</div>
      </PageContentBody>
    </PageContent>
  )
}

export default NodeInvites;
