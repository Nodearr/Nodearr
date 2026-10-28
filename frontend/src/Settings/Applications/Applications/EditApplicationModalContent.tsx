import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { AnyAction } from 'redux';
import { ApplicationAppState } from 'App/State/SettingsAppState';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import {
  saveApplication,
  setApplicationFieldValue,
  setApplicationValue,
  testApplication,
} from 'Store/Actions/settingsActions';
import { createProviderSettingsSelectorHook } from 'Store/Selectors/createProviderSettingsSelector';
import Application, { ApplicationSyncLevel } from 'typings/Application';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import styles from './EditApplicationModalContent.css';

type InputActionCreator = (change: InputChanged) => AnyAction;

const setApplicationValueAction =
  setApplicationValue as unknown as InputActionCreator;
const setApplicationFieldValueAction =
  setApplicationFieldValue as unknown as InputActionCreator;

const syncLevelOptions: EnhancedSelectInputValue<ApplicationSyncLevel>[] = [
  {
    key: ApplicationSyncLevel.Disabled,
    get value() {
      return translate('Disabled');
    },
  },
  {
    key: ApplicationSyncLevel.AddOnly,
    get value() {
      return translate('AddRemoveOnly');
    },
  },
  {
    key: ApplicationSyncLevel.FullSync,
    get value() {
      return translate('FullSync');
    },
  },
];

export interface EditApplicationModalContentProps {
  id?: number;
  onModalClose: () => void;
  onDeleteApplicationPress?: () => void;
}

function EditApplicationModalContent({
  id,
  onModalClose,
  onDeleteApplicationPress,
}: EditApplicationModalContentProps) {
  const dispatch = useDispatch();
  const advancedSettings = useShowAdvancedSettings();

  const {
    isFetching,
    error,
    isSaving,
    isTesting = false,
    saveError,
    item,
    validationErrors,
    validationWarnings,
  } = useSelector(
    createProviderSettingsSelectorHook<Application, ApplicationAppState>(
      'applications',
      id
    )
  );

  const wasSaving = usePrevious(isSaving);

  const { implementationName, name, syncLevel, fields = [], message } = item;

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      dispatch(setApplicationValueAction(change));
    },
    [dispatch]
  );

  const handleFieldChange = useCallback(
    (change: InputChanged) => {
      dispatch(setApplicationFieldValueAction(change));
    },
    [dispatch]
  );

  const handleTestPress = useCallback(() => {
    dispatch(testApplication({ id }));
  }, [id, dispatch]);

  const handleSavePress = useCallback(() => {
    dispatch(saveApplication({ id }));
  }, [id, dispatch]);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, onModalClose, saveError, wasSaving]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader helpTopic="apps">
        {id
          ? translate('EditApplicationImplementation', {
              implementationName: implementationName ?? '',
            })
          : translate('AddApplicationImplementation', {
              implementationName: implementationName ?? '',
            })}
      </ModalHeader>

      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && !!error ? (
          <Alert kind={kinds.DANGER}>
            {translate('UnableToAddANewApplicationPleaseTryAgain')}
          </Alert>
        ) : null}

        {!isFetching && !error ? (
          <Form
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            {message ? (
              <Alert className={styles.message} kind={message.value.type}>
                {message.value.message}
              </Alert>
            ) : null}

            <FormGroup>
              <FormLabel>{translate('Name')}</FormLabel>

              <FormInputGroup
                type={inputTypes.TEXT}
                name="name"
                {...name}
                onChange={handleInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('SyncLevel')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                values={syncLevelOptions}
                name="syncLevel"
                helpTexts={[
                  translate('SyncLevelAddRemove'),
                  translate('SyncLevelFull'),
                ]}
                {...syncLevel}
                onChange={handleInputChange}
              />
            </FormGroup>

            {/* No Tags field: Prowlarr matched indexers to apps by tag, and Nodearr has one indexer. */}
            {fields.map((field) => {
              if (
                field.name === 'includeAutomaticUpgrades' &&
                !fields.find(
                  (item) => item.name === 'fetchNewFilesAutomatically'
                )?.value
              ) {
                return null;
              }

              return (
                <ProviderFieldFormGroup
                  key={field.name}
                  advancedSettings={advancedSettings}
                  provider="application"
                  providerData={item}
                  {...field}
                  onChange={handleFieldChange}
                />
              );
            })}
          </Form>
        ) : null}
      </ModalBody>

      <ModalFooter>
        {id ? (
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeleteApplicationPress}
          >
            {translate('Delete')}
          </Button>
        ) : null}

        <AdvancedSettingsButton showLabel={false} />

        <SpinnerErrorButton
          isSpinning={isTesting}
          error={saveError}
          onPress={handleTestPress}
        >
          {translate('Test')}
        </SpinnerErrorButton>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditApplicationModalContent;
