import React from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes, sizes } from 'Helpers/Props';
import FixedSetting from 'Settings/FixedSetting';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { GeneralSettingsModel } from './useGeneralSettings';

interface UpdateSettingsProps {
  contactNodearr: PendingSection<GeneralSettingsModel>['contactNodearr'];
  updateCheck: PendingSection<GeneralSettingsModel>['updateCheck'];
  updateAutomatically: PendingSection<GeneralSettingsModel>['updateAutomatically'];
  isFixed: (name: string) => boolean;
  onInputChange: (change: InputChanged) => void;
}

interface SwitchProps {
  name: string;
  label: string;
  helpText: string;
  setting: PendingSection<GeneralSettingsModel>['updateCheck'];
  isFixed: (name: string) => boolean;
  isDisabled?: boolean;
  onInputChange: (change: InputChanged) => void;
}

function Switch({
  name,
  label,
  helpText,
  setting,
  isFixed,
  isDisabled = false,
  onInputChange,
}: SwitchProps) {
  return (
    <FormGroup size={sizes.MEDIUM}>
      <FormLabel>{label}</FormLabel>

      {isFixed(name) ? (
        <FixedSetting
          value={setting.value ? translate('Yes') : translate('No')}
        />
      ) : (
        <FormInputGroup
          type={inputTypes.CHECK}
          name={name}
          helpText={helpText}
          isDisabled={isDisabled}
          onChange={onInputChange}
          {...setting}
        />
      )}
    </FormGroup>
  );
}

// What this node may ask nodearr.com, and what it does with a new version (ADR 0024).
function UpdateSettings({
  contactNodearr,
  updateCheck,
  updateAutomatically,
  isFixed,
  onInputChange,
}: UpdateSettingsProps) {
  const isContactOff = !contactNodearr.value;

  return (
    <FieldSet legend={translate('Updates')} helpTopic="updateSettings">
      <Switch
        name="contactNodearr"
        label={translate('ContactNodearr')}
        helpText={translate('ContactNodearrHelpText')}
        setting={contactNodearr}
        isFixed={isFixed}
        onInputChange={onInputChange}
      />

      <Switch
        name="updateCheck"
        label={translate('CheckForUpdates')}
        helpText={
          isContactOff
            ? translate('UpdateCheckContactOffHelpText')
            : translate('CheckForUpdatesHelpText')
        }
        setting={updateCheck}
        isFixed={isFixed}
        isDisabled={isContactOff}
        onInputChange={onInputChange}
      />

      <Switch
        name="updateAutomatically"
        label={translate('InstallUpdatesAutomatically')}
        helpText={translate('InstallUpdatesAutomaticallyHelpText')}
        setting={updateAutomatically}
        isFixed={isFixed}
        isDisabled={isContactOff || !updateCheck.value}
        onInputChange={onInputChange}
      />
    </FieldSet>
  );
}

export default UpdateSettings;
