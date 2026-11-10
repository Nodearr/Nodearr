import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import { inputTypes, sizes } from 'Helpers/Props';
import FixedSetting from 'Settings/FixedSetting';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import UsageReportModal from './UsageReportModal';
import { GeneralSettingsModel } from './useGeneralSettings';

interface UsageSettingsProps {
  contactNodearr: PendingSection<GeneralSettingsModel>['contactNodearr'];
  usageSend: PendingSection<GeneralSettingsModel>['usageSend'];
  isFixed: (name: string) => boolean;
  onInputChange: (change: InputChanged) => void;
}

// The anonymous usage report sent to usage.nodearr.com, and the report itself (ADR 0024).
function UsageSettings({
  contactNodearr,
  usageSend,
  isFixed,
  onInputChange,
}: UsageSettingsProps) {
  const [isReportOpen, setIsReportOpen] = useState(false);
  const isContactOff = !contactNodearr.value;

  const handleShowReportPress = useCallback(() => {
    setIsReportOpen(true);
  }, []);

  const handleReportClose = useCallback(() => {
    setIsReportOpen(false);
  }, []);

  return (
    <FieldSet legend={translate('UsageData')} helpTopic="usageSettings">
      <FormGroup size={sizes.MEDIUM}>
        <FormLabel>{translate('SendAnonymousUsageData')}</FormLabel>

        {isFixed('usageSend') ? (
          <FixedSetting
            value={usageSend.value ? translate('Yes') : translate('No')}
          />
        ) : (
          <FormInputGroup
            type={inputTypes.CHECK}
            name="usageSend"
            helpText={
              isContactOff
                ? translate('UpdateCheckContactOffHelpText')
                : translate('SendAnonymousUsageDataHelpText')
            }
            helpLink="https://nodearr.com/privacy"
            isDisabled={isContactOff}
            onChange={onInputChange}
            {...usageSend}
          />
        )}
      </FormGroup>

      <FormGroup size={sizes.MEDIUM}>
        <FormLabel>{translate('UsageReport')}</FormLabel>

        <Button onPress={handleShowReportPress}>
          {translate('ShowReport')}
        </Button>
      </FormGroup>

      <UsageReportModal
        isOpen={isReportOpen}
        onModalClose={handleReportClose}
      />
    </FieldSet>
  );
}

export default UsageSettings;
