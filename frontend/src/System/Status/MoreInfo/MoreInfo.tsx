import React from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItemDescription from 'Components/DescriptionList/DescriptionListItemDescription';
import DescriptionListItemTitle from 'Components/DescriptionList/DescriptionListItemTitle';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';

function MoreInfo() {
  return (
    <FieldSet legend={translate('MoreInfo')}>
      <DescriptionList>
        <DescriptionListItemTitle>
          {translate('HomePage')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://nodearr.com/">nodearr.com</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>{translate('Wiki')}</DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://wiki.nodearr.com/">wiki.nodearr.com/</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Forums')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://forums.nodearr.com/">forums.nodearr.com</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Discord')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://discord.nodearr.com/">discord.nodearr.com</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>{translate('IRC')}</DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="irc://irc.libera.chat/#nodearr">
            {translate('IRCLinkText')}
          </Link>
        </DescriptionListItemDescription>
        <DescriptionListItemDescription>
          <Link to="https://web.libera.chat/?channels=#nodearr">
            {translate('LiberaWebchat')}
          </Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Donations')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://nodearr.com/donate">nodearr.com/donate</Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('Source')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://github.com/Nodearr/Nodearr/">
            github.com/Nodearr/Nodearr
          </Link>
        </DescriptionListItemDescription>

        <DescriptionListItemTitle>
          {translate('FeatureRequests')}
        </DescriptionListItemTitle>
        <DescriptionListItemDescription>
          <Link to="https://forums.nodearr.com/">forums.nodearr.com</Link>
        </DescriptionListItemDescription>
        <DescriptionListItemDescription>
          <Link to="https://github.com/Nodearr/Nodearr/issues">
            github.com/Nodearr/Nodearr/issues
          </Link>
        </DescriptionListItemDescription>
      </DescriptionList>
    </FieldSet>
  );
}

export default MoreInfo;
