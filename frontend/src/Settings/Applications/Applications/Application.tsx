import React, { useCallback, useMemo, useState } from 'react';
import { useDispatch } from 'react-redux';
import Card from 'Components/Card';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import SeriesTagList from 'Components/SeriesTagList';
import { icons, kinds } from 'Helpers/Props';
import { deleteApplication } from 'Store/Actions/settingsActions';
import ApplicationModel, { ApplicationSyncLevel } from 'typings/Application';
import translate from 'Utilities/String/translate';
import EditApplicationModal from './EditApplicationModal';
import styles from './Application.css';

function Application({
  id,
  name,
  enable,
  syncLevel,
  fields,
  tags,
}: ApplicationModel) {
  const dispatch = useDispatch();

  const [isEditApplicationModalOpen, setIsEditApplicationModalOpen] =
    useState(false);
  const [isDeleteApplicationModalOpen, setIsDeleteApplicationModalOpen] =
    useState(false);

  const applicationUrl = useMemo(() => {
    const fieldValue = fields.find((field) => field.name === 'baseUrl')?.value;

    return typeof fieldValue === 'string' && fieldValue.length
      ? fieldValue
      : undefined;
  }, [fields]);

  const handleEditApplicationPress = useCallback(() => {
    setIsEditApplicationModalOpen(true);
  }, []);

  const handleEditApplicationModalClose = useCallback(() => {
    setIsEditApplicationModalOpen(false);
  }, []);

  const handleDeleteApplicationPress = useCallback(() => {
    setIsEditApplicationModalOpen(false);
    setIsDeleteApplicationModalOpen(true);
  }, []);

  const handleDeleteApplicationModalClose = useCallback(() => {
    setIsDeleteApplicationModalOpen(false);
  }, []);

  const handleConfirmDeleteApplication = useCallback(() => {
    dispatch(deleteApplication({ id }));
  }, [id, dispatch]);

  return (
    <Card
      className={styles.application}
      overlayContent={true}
      onPress={handleEditApplicationPress}
    >
      <div className={styles.nameContainer}>
        <div className={styles.name}>{name}</div>

        {enable && applicationUrl ? (
          <IconButton
            className={styles.externalLink}
            name={icons.EXTERNAL_LINK}
            title={translate('GoToApplication')}
            to={applicationUrl}
          />
        ) : null}
      </div>

      {syncLevel === ApplicationSyncLevel.AddOnly ? (
        <Label kind={kinds.WARNING}>{translate('AddRemoveOnly')}</Label>
      ) : null}

      {syncLevel === ApplicationSyncLevel.FullSync ? (
        <Label kind={kinds.SUCCESS}>{translate('FullSync')}</Label>
      ) : null}

      {syncLevel === ApplicationSyncLevel.Disabled ? (
        <Label kind={kinds.DISABLED} outline={true}>
          {translate('Disabled')}
        </Label>
      ) : null}

      <SeriesTagList tags={tags} />

      <EditApplicationModal
        id={id}
        isOpen={isEditApplicationModalOpen}
        onModalClose={handleEditApplicationModalClose}
        onDeleteApplicationPress={handleDeleteApplicationPress}
      />

      <ConfirmModal
        isOpen={isDeleteApplicationModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteApplication')}
        message={translate('DeleteApplicationMessageText', { name })}
        confirmLabel={translate('Delete')}
        onConfirm={handleConfirmDeleteApplication}
        onCancel={handleDeleteApplicationModalClose}
      />
    </Card>
  );
}

export default Application;
