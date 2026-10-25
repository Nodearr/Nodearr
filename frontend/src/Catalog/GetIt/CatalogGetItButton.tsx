import React, { useCallback, useState } from 'react';
import { useCommand } from 'Commands/useCommands';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { MediaKind } from '../useSharing';
import CatalogGetItModal from './CatalogGetItModal';
import { CatalogGetItScope } from './useCatalogGetIt';

interface CatalogGetItButtonProps {
  itemId: number;
  scope: CatalogGetItScope;
  mediaKind: MediaKind;
  title: string;
}

function CatalogGetItButton({
  itemId,
  scope,
  mediaKind,
  title,
}: CatalogGetItButtonProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [commandId, setCommandId] = useState<number | null>(null);
  const command = useCommand('CatalogGetIt', { itemId });
  const isWorking =
    command?.status === 'queued' || command?.status === 'started';
  const handlePress = useCallback(() => {
    setCommandId(isWorking ? command.id : null);
    setIsOpen(true);
  }, [command, isWorking]);
  const handleClose = useCallback(() => setIsOpen(false), []);

  return (
    <>
      <Button onPress={handlePress}>
        {isWorking ? <Icon name={icons.SPINNER} isSpinning={true} /> : null}{' '}
        {translate('GetIt')}
      </Button>
      {isOpen ? (
        <CatalogGetItModal
          selection={{ itemId, scope }}
          mediaKind={mediaKind}
          title={title}
          initialCommandId={commandId}
          onModalClose={handleClose}
        />
      ) : null}
    </>
  );
}

export default CatalogGetItButton;
