import React, { useCallback, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import { ApplicationAppState } from 'App/State/SettingsAppState';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import {
  bulkDeleteApplications,
  bulkEditApplications,
  setManageApplicationsSort,
} from 'Store/Actions/settingsActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import Application from 'typings/Application';
import { CheckInputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import ManageApplicationsEditModal from './Edit/ManageApplicationsEditModal';
import ManageApplicationsModalRow from './ManageApplicationsModalRow';
import TagsModal from './Tags/TagsModal';
import styles from './ManageApplicationsModalContent.css';

const COLUMNS: Column[] = [
  {
    name: 'name',
    label: () => translate('Name'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'implementation',
    label: () => translate('Implementation'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'syncLevel',
    label: () => translate('SyncLevel'),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'tags',
    label: () => translate('Tags'),
    isSortable: true,
    isVisible: true,
  },
];

interface ManageApplicationsModalContentProps {
  onModalClose(): void;
}

interface ManageApplicationsModalContentInnerProps {
  onModalClose: () => void;
}

function ManageApplicationsModalContentInner({
  onModalClose,
}: ManageApplicationsModalContentInnerProps) {
  const dispatch = useDispatch();
  const {
    isFetching,
    isPopulated,
    isDeleting,
    isSaving,
    error,
    items,
    sortKey,
    sortDirection,
  }: ApplicationAppState = useSelector(
    createClientSideCollectionSelector(
      'settings.applications',
      'manageApplications'
    )
  );

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isTagsModalOpen, setIsTagsModalOpen] = useState(false);
  const [isSavingTags, setIsSavingTags] = useState(false);

  const {
    allSelected,
    allUnselected,
    anySelected,
    selectedCount,
    getSelectedIds,
    selectAll,
    unselectAll,
    useSelectedIds,
  } = useSelect<Application>();

  const selectedIds = useSelectedIds();

  const onSortPress = useCallback(
    (value: string) => {
      dispatch(setManageApplicationsSort({ sortKey: value }));
    },
    [dispatch]
  );

  const onDeletePress = useCallback(() => {
    setIsDeleteModalOpen(true);
  }, [setIsDeleteModalOpen]);

  const onDeleteModalClose = useCallback(() => {
    setIsDeleteModalOpen(false);
  }, [setIsDeleteModalOpen]);

  const onEditPress = useCallback(() => {
    setIsEditModalOpen(true);
  }, [setIsEditModalOpen]);

  const onEditModalClose = useCallback(() => {
    setIsEditModalOpen(false);
  }, [setIsEditModalOpen]);

  const onConfirmDelete = useCallback(() => {
    dispatch(bulkDeleteApplications({ ids: getSelectedIds() }));
    setIsDeleteModalOpen(false);
  }, [getSelectedIds, dispatch]);

  const onSavePress = useCallback(
    (payload: object) => {
      setIsEditModalOpen(false);

      dispatch(
        bulkEditApplications({
          ids: getSelectedIds(),
          ...payload,
        })
      );
    },
    [getSelectedIds, dispatch]
  );

  const onTagsPress = useCallback(() => {
    setIsTagsModalOpen(true);
  }, [setIsTagsModalOpen]);

  const onTagsModalClose = useCallback(() => {
    setIsTagsModalOpen(false);
  }, [setIsTagsModalOpen]);

  const onApplyTagsPress = useCallback(
    (tags: number[], applyTags: string) => {
      setIsSavingTags(true);
      setIsTagsModalOpen(false);

      dispatch(
        bulkEditApplications({
          ids: getSelectedIds(),
          tags,
          applyTags,
        })
      );
    },
    [getSelectedIds, dispatch]
  );

  const onSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      if (value) {
        selectAll();
      } else {
        unselectAll();
      }
    },
    [selectAll, unselectAll]
  );

  const errorMessage = getErrorMessage(
    error,
    'Unable to load applications.'
  );

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('ManageApplications')}</ModalHeader>
      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {error ? <div>{errorMessage}</div> : null}

        {isPopulated && !error && !items.length && (
          <Alert kind={kinds.INFO}>{translate('NoApplicationsFound')}</Alert>
        )}

        {isPopulated && !!items.length && !isFetching && !isFetching ? (
          <Table
            columns={COLUMNS}
            horizontalScroll={true}
            selectAll={true}
            allSelected={allSelected}
            allUnselected={allUnselected}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSelectAllChange={onSelectAllChange}
            onSortPress={onSortPress}
          >
            <TableBody>
              {items.map((item) => {
                return (
                  <ManageApplicationsModalRow
                    key={item.id}
                    {...item}
                    columns={COLUMNS}
                  />
                );
              })}
            </TableBody>
          </Table>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <div className={styles.leftButtons}>
          <SpinnerButton
            kind={kinds.DANGER}
            isSpinning={isDeleting}
            isDisabled={!anySelected}
            onPress={onDeletePress}
          >
            {translate('Delete')}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={isSaving}
            isDisabled={!anySelected}
            onPress={onEditPress}
          >
            {translate('Edit')}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={isSaving && isSavingTags}
            isDisabled={!anySelected}
            onPress={onTagsPress}
          >
            {translate('SetTags')}
          </SpinnerButton>
        </div>

        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>

      <ManageApplicationsEditModal
        isOpen={isEditModalOpen}
        applicationIds={selectedIds}
        onModalClose={onEditModalClose}
        onSavePress={onSavePress}
      />

      <TagsModal
        isOpen={isTagsModalOpen}
        ids={selectedIds}
        onApplyTagsPress={onApplyTagsPress}
        onModalClose={onTagsModalClose}
      />

      <ConfirmModal
        isOpen={isDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteSelectedApplications')}
        message={translate('DeleteSelectedApplicationsMessageText', {
          count: selectedCount,
        })}
        confirmLabel={translate('Delete')}
        onConfirm={onConfirmDelete}
        onCancel={onDeleteModalClose}
      />
    </ModalContent>
  );
}

function ManageApplicationsModalContent({
  onModalClose,
}: ManageApplicationsModalContentProps) {
  const { items }: ApplicationAppState = useSelector(
    createClientSideCollectionSelector(
      'settings.applications',
      'manageApplications'
    )
  );

  return (
    <SelectProvider items={items}>
      <ManageApplicationsModalContentInner onModalClose={onModalClose} />
    </SelectProvider>
  );
}

export default ManageApplicationsModalContent;
