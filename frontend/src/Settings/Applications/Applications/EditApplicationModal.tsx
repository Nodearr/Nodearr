import React, { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  cancelSaveApplication,
  cancelTestApplication,
} from 'Store/Actions/settingsActions';
import EditApplicationModalContent, {
  EditApplicationModalContentProps,
} from './EditApplicationModalContent';

const section = 'settings.applications';

interface EditApplicationModalProps extends EditApplicationModalContentProps {
  isOpen: boolean;
}

function EditApplicationModal({
  isOpen,
  onModalClose,
  ...otherProps
}: EditApplicationModalProps) {
  const dispatch = useDispatch();

  const handleModalClose = useCallback(() => {
    dispatch(clearPendingChanges({ section }));
    dispatch(cancelTestApplication({ section }));
    dispatch(cancelSaveApplication({ section }));

    onModalClose();
  }, [dispatch, onModalClose]);

  return (
    <Modal size={sizes.MEDIUM} isOpen={isOpen} onModalClose={handleModalClose}>
      <EditApplicationModalContent
        {...otherProps}
        onModalClose={handleModalClose}
      />
    </Modal>
  );
}

export default EditApplicationModal;
