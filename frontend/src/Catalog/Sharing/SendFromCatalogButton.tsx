import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds } from 'Helpers/Props';
import { getErrorMessage, usePeers } from 'Nodes/useNodes';
import translate from 'Utilities/String/translate';
import { ShareKey, titleId, TitleKey, useShareableTitles } from '../useSharing';
import SendToFriend from './SendToFriend';

function SendFromCatalogDialog({
  titleKey,
  items,
  onClose,
}: {
  titleKey: TitleKey;
  items: ShareKey[];
  onClose: () => void;
}) {
  const {
    data: titles = [],
    isLoading: areTitlesLoading,
    error: titlesError,
  } = useShareableTitles();
  const {
    data: peers = [],
    isLoading: arePeersLoading,
    error: peersError,
  } = usePeers();

  const title = titles.find((item) => titleId(item) === titleId(titleKey));

  if (
    !areTitlesLoading &&
    !arePeersLoading &&
    !titlesError &&
    !peersError &&
    title
  ) {
    return (
      <SendToFriend
        titles={titles}
        peers={peers}
        initialTitle={title}
        initialItems={items}
        isModal={true}
        onModalClose={onClose}
      />
    );
  }

  return (
    <Modal isOpen={true} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>{translate('SendToFriend')}</ModalHeader>
        <ModalBody>
          {areTitlesLoading || arePeersLoading ? <LoadingIndicator /> : null}
          {titlesError || peersError ? (
            <Alert kind={kinds.DANGER}>
              {getErrorMessage(titlesError ?? peersError)}
            </Alert>
          ) : null}
          {!areTitlesLoading &&
          !arePeersLoading &&
          !titlesError &&
          !peersError &&
          !title ? (
            <Alert kind={kinds.INFO}>
              {translate('SendCatalogTitleUnavailable')}
            </Alert>
          ) : null}
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}

function SendFromCatalogButton({
  titleKey,
  items,
  label,
  className,
  showLabel = false,
}: {
  titleKey: TitleKey;
  items: ShareKey[];
  label: string;
  className?: string;
  showLabel?: boolean;
}) {
  const [isOpen, setIsOpen] = useState(false);

  const handleOpenPress = useCallback(() => {
    setIsOpen(true);
  }, []);

  const handleClose = useCallback(() => {
    setIsOpen(false);
  }, []);

  return (
    <>
      {showLabel ? (
        <Button kind={kinds.PRIMARY} onPress={handleOpenPress}>
          {translate('SendToFriend')}
        </Button>
      ) : (
        <IconButton
          className={className}
          name={icons.SEND}
          title={translate('SendTitleToFriend', { title: label })}
          aria-label={translate('SendTitleToFriend', { title: label })}
          onPress={handleOpenPress}
        />
      )}
      {isOpen ? (
        <SendFromCatalogDialog
          titleKey={titleKey}
          items={items}
          onClose={handleClose}
        />
      ) : null}
    </>
  );
}

export default SendFromCatalogButton;
