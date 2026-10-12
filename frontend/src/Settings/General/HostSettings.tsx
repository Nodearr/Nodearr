import React from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes, sizes } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import FixedSetting from 'Settings/FixedSetting';
import { useIsWindowsService } from 'System/Status/useSystemStatus';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { GeneralSettingsModel } from './useGeneralSettings';

interface HostSettingsProps {
  bindAddress: PendingSection<GeneralSettingsModel>['bindAddress'];
  port: PendingSection<GeneralSettingsModel>['port'];
  urlBase: PendingSection<GeneralSettingsModel>['urlBase'];
  instanceName: PendingSection<GeneralSettingsModel>['instanceName'];
  applicationUrl: PendingSection<GeneralSettingsModel>['applicationUrl'];
  enableSsl: PendingSection<GeneralSettingsModel>['enableSsl'];
  sslPort: PendingSection<GeneralSettingsModel>['sslPort'];
  sslKeyPath: PendingSection<GeneralSettingsModel>['sslKeyPath'];
  sslCertPath: PendingSection<GeneralSettingsModel>['sslCertPath'];
  sslCertPassword: PendingSection<GeneralSettingsModel>['sslCertPassword'];
  launchBrowser: PendingSection<GeneralSettingsModel>['launchBrowser'];
  isFixed: (name: string) => boolean;
  onInputChange: (change: InputChanged) => void;
}

function HostSettings({
  bindAddress,
  port,
  urlBase,
  instanceName,
  applicationUrl,
  enableSsl,
  sslPort,
  sslCertPath,
  sslKeyPath,
  sslCertPassword,
  launchBrowser,
  isFixed,
  onInputChange,
}: HostSettingsProps) {
  const showAdvancedSettings = useShowAdvancedSettings();
  const isWindowsService = useIsWindowsService();

  return (
    <FieldSet legend={translate('Host')}>
      <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
        <FormLabel>{translate('BindAddress')}</FormLabel>

        {isFixed('bindAddress') ? (
          <FixedSetting value={bindAddress.value} />
        ) : (
          <FormInputGroup
            type={inputTypes.TEXT}
            name="bindAddress"
            helpText={translate('BindAddressHelpText')}
            helpTextWarning={translate('RestartRequiredHelpTextWarning')}
            onChange={onInputChange}
            {...bindAddress}
          />
        )}
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('PortNumber')}</FormLabel>

        {isFixed('port') ? (
          <FixedSetting value={port.value} />
        ) : (
          <FormInputGroup
            type={inputTypes.NUMBER}
            name="port"
            min={1}
            max={65535}
            autocomplete="off"
            helpTextWarning={translate('RestartRequiredHelpTextWarning')}
            onChange={onInputChange}
            {...port}
          />
        )}
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('UrlBase')}</FormLabel>

        {isFixed('urlBase') ? (
          <FixedSetting value={urlBase.value || '/'} />
        ) : (
          <FormInputGroup
            type={inputTypes.TEXT}
            name="urlBase"
            helpText={translate('UrlBaseHelpText')}
            helpTextWarning={translate('RestartRequiredHelpTextWarning')}
            onChange={onInputChange}
            {...urlBase}
          />
        )}
      </FormGroup>

      <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
        <FormLabel>{translate('InstanceName')}</FormLabel>

        {isFixed('instanceName') ? (
          <FixedSetting value={instanceName.value} />
        ) : (
          <FormInputGroup
            type={inputTypes.TEXT}
            name="instanceName"
            helpText={translate('InstanceNameHelpText')}
            helpTextWarning={translate('RestartRequiredHelpTextWarning')}
            onChange={onInputChange}
            {...instanceName}
          />
        )}
      </FormGroup>

      <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
        <FormLabel>{translate('ApplicationURL')}</FormLabel>

        <FormInputGroup
          type={inputTypes.TEXT}
          name="applicationUrl"
          helpText={translate('ApplicationUrlHelpText')}
          onChange={onInputChange}
          {...applicationUrl}
        />
      </FormGroup>

      <FormGroup
        advancedSettings={showAdvancedSettings}
        isAdvanced={true}
        size={sizes.MEDIUM}
      >
        <FormLabel>{translate('EnableSsl')}</FormLabel>

        {isFixed('enableSsl') ? (
          <FixedSetting
            value={enableSsl.value ? translate('Yes') : translate('No')}
          />
        ) : (
          <FormInputGroup
            type={inputTypes.CHECK}
            name="enableSsl"
            helpText={translate('EnableSslHelpText')}
            onChange={onInputChange}
            {...enableSsl}
          />
        )}
      </FormGroup>

      {enableSsl.value ? (
        <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
          <FormLabel>{translate('SslPort')}</FormLabel>

          {isFixed('sslPort') ? (
            <FixedSetting value={sslPort.value} />
          ) : (
            <FormInputGroup
              type={inputTypes.NUMBER}
              name="sslPort"
              min={1}
              max={65535}
              helpTextWarning={translate('RestartRequiredHelpTextWarning')}
              onChange={onInputChange}
              {...sslPort}
            />
          )}
        </FormGroup>
      ) : null}

      {enableSsl.value ? (
        <>
          <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
            <FormLabel>{translate('SslCertPath')}</FormLabel>

            {isFixed('sslCertPath') ? (
              <FixedSetting value={sslCertPath.value} />
            ) : (
              <FormInputGroup
                type={inputTypes.TEXT}
                name="sslCertPath"
                helpText={translate('SslCertPathHelpText')}
                helpTextWarning={translate('RestartRequiredHelpTextWarning')}
                onChange={onInputChange}
                {...sslCertPath}
              />
            )}
          </FormGroup>

          <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
            <FormLabel>{translate('SslKeyPath')}</FormLabel>

            {isFixed('sslKeyPath') ? (
              <FixedSetting value={sslKeyPath.value} />
            ) : (
              <FormInputGroup
                type={inputTypes.TEXT}
                name="sslKeyPath"
                helpText={translate('SslKeyPathHelpText')}
                helpTextWarning={translate('RestartRequiredHelpTextWarning')}
                onChange={onInputChange}
                {...sslKeyPath}
              />
            )}
          </FormGroup>

          <FormGroup advancedSettings={showAdvancedSettings} isAdvanced={true}>
            <FormLabel>{translate('SslCertPassword')}</FormLabel>

            {isFixed('sslCertPassword') ? (
              <FixedSetting value="********" />
            ) : (
              <FormInputGroup
                type={inputTypes.PASSWORD}
                name="sslCertPassword"
                helpText={translate('SslCertPasswordHelpText')}
                helpTextWarning={translate('RestartRequiredHelpTextWarning')}
                onChange={onInputChange}
                {...sslCertPassword}
              />
            )}
          </FormGroup>
        </>
      ) : null}

      {isWindowsService ? null : (
        <FormGroup size={sizes.MEDIUM}>
          <FormLabel>{translate('OpenBrowserOnStart')}</FormLabel>

          {isFixed('launchBrowser') ? (
            <FixedSetting
              value={launchBrowser.value ? translate('Yes') : translate('No')}
            />
          ) : (
            <FormInputGroup
              type={inputTypes.CHECK}
              name="launchBrowser"
              helpText={translate('OpenBrowserOnStartHelpText')}
              onChange={onInputChange}
              {...launchBrowser}
            />
          )}
        </FormGroup>
      )}
    </FieldSet>
  );
}

export default HostSettings;
