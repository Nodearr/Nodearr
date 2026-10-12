import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { inputTypes, kinds } from 'Helpers/Props';
import FixedSetting from 'Settings/FixedSetting';
import SettingsToolbar from 'Settings/SettingsToolbar';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { RelayMode, useManageNodeSettings } from './useNodeSettings';

const relayOptions: EnhancedSelectInputValue<RelayMode>[] = [
  {
    key: 'n0',
    get value() {
      return translate('NodeSettingsRelayPublic');
    },
  },
  {
    key: 'custom',
    get value() {
      return translate('NodeSettingsRelayCustom');
    },
  },
  {
    key: 'disabled',
    get value() {
      return translate('NodeSettingsRelayDisabled');
    },
  },
];

function NodeSettings() {
  const {
    isFetching,
    isFetched,
    error,
    hasPendingChanges,
    hasSettings,
    settings,
    isSaving,
    validationErrors,
    validationWarnings,
    saveSettings,
    updateSetting,
  } = useManageNodeSettings();

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error name needs to be keyof NodeSettingsModel
      updateSetting(change.name, change.value);
    },
    [updateSetting]
  );

  const handleSavePress = useCallback(() => {
    saveSettings();
  }, [saveSettings]);

  const fixedNames = hasSettings
    ? (settings.fixedByEnvironment?.value ?? []).map((name) =>
        name.toLowerCase()
      )
    : [];
  const isFixed = (name: string) => fixedNames.includes(name.toLowerCase());

  return (
    <PageContent title={translate('NodeSettings')}>
      <SettingsToolbar
        hasPendingChanges={hasPendingChanges}
        isSaving={isSaving}
        onSavePress={handleSavePress}
      />

      <PageContentBody>
        {isFetching && !isFetched ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>
            {translate('NodeSettingsLoadError')}
          </Alert>
        ) : null}

        {hasSettings && isFetched && !error ? (
          <Form
            id="nodeSettings"
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            {settings.transportRestarted.value &&
            settings.transportRunning.value ? (
              <Alert kind={kinds.SUCCESS}>
                {translate('NodeSettingsTransportRestarted')}
              </Alert>
            ) : null}

            {settings.transportRestarted.value &&
            !settings.transportRunning.value ? (
              <Alert kind={kinds.DANGER}>
                {translate('NodeSettingsTransportDidNotStart')}
              </Alert>
            ) : null}

            {fixedNames.length > 0 ? (
              <Alert kind={kinds.INFO}>
                {translate('NodeSettingsSomeFixed')}
              </Alert>
            ) : null}

            <FieldSet legend={translate('NodeSettingsIdentity')}>
              <FormGroup>
                <FormLabel>{translate('NodeSettingsName')}</FormLabel>

                {isFixed('nodeName') ? (
                  <FixedSetting value={settings.nodeName.value} />
                ) : (
                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="nodeName"
                    placeholder={settings.defaultNodeName.value}
                    helpText={translate('NodeSettingsNameHelpText')}
                    onChange={handleInputChange}
                    {...settings.nodeName}
                  />
                )}
              </FormGroup>
            </FieldSet>

            <FieldSet legend={translate('NodeSettingsReaching')}>
              <FormGroup>
                <FormLabel>{translate('NodeSettingsRelay')}</FormLabel>

                {isFixed('relay') ? (
                  <FixedSetting
                    value={
                      relayOptions.find((o) => o.key === settings.relay.value)
                        ?.value
                    }
                  />
                ) : (
                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="relay"
                    values={relayOptions}
                    helpText={translate('NodeSettingsRelayHelpText')}
                    helpTextWarning={
                      settings.relay.value === 'disabled'
                        ? translate('NodeSettingsRelayDisabledWarning')
                        : undefined
                    }
                    onChange={handleInputChange}
                    {...settings.relay}
                  />
                )}
              </FormGroup>

              {settings.relay.value === 'custom' ? (
                <FormGroup>
                  <FormLabel>{translate('NodeSettingsRelayUrl')}</FormLabel>

                  {isFixed('relayUrl') ? (
                    <FixedSetting value={settings.relayUrl.value} />
                  ) : (
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="relayUrl"
                      placeholder="https://relay.example.com"
                      helpText={translate('NodeSettingsRelayUrlHelpText')}
                      onChange={handleInputChange}
                      {...settings.relayUrl}
                    />
                  )}
                </FormGroup>
              ) : null}

              <FormGroup>
                <FormLabel>{translate('NodeSettingsPort')}</FormLabel>

                {isFixed('port') ? (
                  <FixedSetting value={settings.port.value} />
                ) : (
                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="port"
                    min={0}
                    max={65535}
                    helpText={translate('NodeSettingsPortHelpText')}
                    onChange={handleInputChange}
                    {...settings.port}
                  />
                )}
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('NodeSettingsPortMapping')}</FormLabel>

                {isFixed('portMapping') ? (
                  <FixedSetting
                    value={
                      settings.portMapping.value
                        ? translate('Yes')
                        : translate('No')
                    }
                  />
                ) : (
                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="portMapping"
                    helpText={translate('NodeSettingsPortMappingHelpText')}
                    onChange={handleInputChange}
                    {...settings.portMapping}
                  />
                )}
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('NodeSettingsDirectTimeout')}</FormLabel>

                {isFixed('directTimeoutSeconds') ? (
                  <FixedSetting value={settings.directTimeoutSeconds.value} />
                ) : (
                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="directTimeoutSeconds"
                    min={5}
                    max={120}
                    unit={translate('NodeSettingsSeconds')}
                    helpText={translate('NodeSettingsDirectTimeoutHelpText')}
                    onChange={handleInputChange}
                    {...settings.directTimeoutSeconds}
                  />
                )}
              </FormGroup>
            </FieldSet>

            <FieldSet legend={translate('Transfers')}>
              <FormGroup>
                <FormLabel>{translate('NodeSettingsDownloadFolder')}</FormLabel>

                {isFixed('downloadFolder') ? (
                  <FixedSetting value={settings.downloadFolder.value} />
                ) : (
                  <FormInputGroup
                    type={inputTypes.PATH}
                    name="downloadFolder"
                    includeFiles={false}
                    placeholder={settings.defaultDownloadFolder.value}
                    helpText={translate('NodeSettingsDownloadFolderHelpText')}
                    onChange={handleInputChange}
                    {...settings.downloadFolder}
                  />
                )}
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('NodeSettingsUploadLimit')}</FormLabel>

                {isFixed('uploadLimitKbps') ? (
                  <FixedSetting value={settings.uploadLimitKbps.value} />
                ) : (
                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="uploadLimitKbps"
                    min={0}
                    unit="KB/s"
                    helpText={translate('NodeSettingsUploadLimitHelpText')}
                    onChange={handleInputChange}
                    {...settings.uploadLimitKbps}
                  />
                )}
              </FormGroup>
            </FieldSet>
          </Form>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default NodeSettings;
