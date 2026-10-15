import React, { useCallback, useState } from 'react';
import { Error } from 'App/State/AppSectionState';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import ApplicationModel from 'typings/Application';
import translate from 'Utilities/String/translate';
import AddApplicationModal from './AddApplicationModal';
import Application from './Application';
import EditApplicationModal from './EditApplicationModal';
import styles from './Applications.css';

interface ApplicationsProps {
  isFetching: boolean;
  isPopulated: boolean;
  error: Error;
  items: ApplicationModel[];
}

function Applications({
  isFetching,
  isPopulated,
  error,
  items,
}: ApplicationsProps) {
  const [isAddApplicationModalOpen, setIsAddApplicationModalOpen] =
    useState(false);
  const [isEditApplicationModalOpen, setIsEditApplicationModalOpen] =
    useState(false);

  const handleAddApplicationPress = useCallback(() => {
    setIsAddApplicationModalOpen(true);
  }, []);

  const handleApplicationSelect = useCallback(() => {
    setIsAddApplicationModalOpen(false);
    setIsEditApplicationModalOpen(true);
  }, []);

  const handleAddApplicationModalClose = useCallback(() => {
    setIsAddApplicationModalOpen(false);
  }, []);

  const handleEditApplicationModalClose = useCallback(() => {
    setIsEditApplicationModalOpen(false);
  }, []);

  return (
    <FieldSet legend={translate('Applications')}>
      <PageSectionContent
        errorMessage={translate('ApplicationsLoadError')}
        error={error}
        isFetching={isFetching}
        isPopulated={isPopulated}
      >
        <div className={styles.applications}>
          {items.map((item) => {
            return <Application key={item.id} {...item} />;
          })}

          <Card
            className={styles.addApplication}
            onPress={handleAddApplicationPress}
          >
            <div className={styles.center}>
              <Icon name={icons.ADD} size={45} />
            </div>
          </Card>
        </div>

        <AddApplicationModal
          isOpen={isAddApplicationModalOpen}
          onApplicationSelect={handleApplicationSelect}
          onModalClose={handleAddApplicationModalClose}
        />

        <EditApplicationModal
          isOpen={isEditApplicationModalOpen}
          onModalClose={handleEditApplicationModalClose}
        />
      </PageSectionContent>
    </FieldSet>
  );
}

export default Applications;
