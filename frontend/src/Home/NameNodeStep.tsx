import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import { NodeSettingsModel } from 'Settings/Node/useNodeSettings';
import { useSaveSettings, useSettings } from 'Settings/useSettings';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './Home.css';

const PATH = '/settings/node';

interface NameNodeStepProps {
  number: number;
  isActive: boolean;
}

// The node has a name of its own once one was chosen, or when the environment fixes it.
export function hasChosenName(settings: Partial<NodeSettingsModel>) {
  return (
    !!settings.nodeName ||
    (settings.fixedByEnvironment ?? []).some(
      (name) => name.toLowerCase() === 'nodename'
    )
  );
}

function NameNodeStep({ number, isActive }: NameNodeStepProps) {
  const queryClient = useQueryClient();
  const { data: settings, isFetched } = useSettings<NodeSettingsModel>(PATH);
  const { save, isSaving, saveError } =
    useSaveSettings<NodeSettingsModel>(PATH);

  const [name, setName] = useState('');
  const [isEditing, setIsEditing] = useState(false);

  const currentName = settings.nodeName || settings.defaultNodeName || '';

  // Starts from the name the node has now, which is the machine's until one is chosen.
  useEffect(() => {
    if (isFetched) {
      setName(currentName);
    }
  }, [isFetched, currentName]);

  const handleNameChange = useCallback(({ value }: InputChanged<string>) => {
    setName(value);
  }, []);

  const handleSavePress = useCallback(() => {
    save(
      { ...settings, nodeName: name.trim() },
      {
        onSuccess: () => {
          setIsEditing(false);

          // The name shown elsewhere comes from the node's identity.
          queryClient.invalidateQueries({ queryKey: ['/nodes/identity'] });
        },
      }
    );
  }, [save, settings, name, queryClient]);

  const handleChangePress = useCallback(() => {
    setIsEditing(true);
  }, []);

  const isDone = isFetched && hasChosenName(settings) && !isEditing;
  const isFixed = (settings.fixedByEnvironment ?? []).some(
    (fixed) => fixed.toLowerCase() === 'nodename'
  );

  return (
    <SetupStep
      number={number}
      title={translate('HomeStepNameTitle')}
      isDone={isDone}
      isActive={isActive || isEditing}
      summary={translate('HomeStepNameDone', { name: currentName })}
      doneActions={
        isFixed ? null : (
          <Button onPress={handleChangePress}>{translate('HomeChange')}</Button>
        )
      }
    >
      <p className={styles.stepText}>{translate('HomeStepNameText')}</p>

      <div className={styles.nameRow}>
        <div className={styles.nameInput}>
          <TextInput
            name="nodeName"
            value={name}
            placeholder={settings.defaultNodeName}
            onChange={handleNameChange}
          />
        </div>

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isSaving}
          isDisabled={!name.trim()}
          onPress={handleSavePress}
        >
          {translate('HomeStepNameSave')}
        </SpinnerButton>
      </div>

      {saveError ? (
        <Alert className={styles.message} kind={kinds.DANGER}>
          {translate('HomeStepNameError')}
        </Alert>
      ) : null}
    </SetupStep>
  );
}

export default NameNodeStep;
