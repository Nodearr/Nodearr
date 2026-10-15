import React from 'react';
import Modal from 'Components/Modal/Modal';
import AddApplicationModalContent, {
  AddApplicationModalContentProps,
} from './AddApplicationModalContent';

interface AddApplicationModalProps extends AddApplicationModalContentProps {
  isOpen: boolean;
}

function AddApplicationModal({
  isOpen,
  onModalClose,
  ...otherProps
}: AddApplicationModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddApplicationModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default AddApplicationModal;
