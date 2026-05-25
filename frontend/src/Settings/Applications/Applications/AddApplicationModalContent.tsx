import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import {
  fetchApplicationSchema,
  selectApplicationSchema,
} from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import AddApplicationItem, {
  AddApplicationSelection,
} from './AddApplicationItem';
import styles from './AddApplicationModalContent.css';

export interface AddApplicationModalContentProps {
  onApplicationSelect: () => void;
  onModalClose: () => void;
}

function AddApplicationModalContent({
  onApplicationSelect,
  onModalClose,
}: AddApplicationModalContentProps) {
  const dispatch = useDispatch();

  const { isSchemaFetching, isSchemaPopulated, schemaError, schema } =
    useSelector((state: AppState) => state.settings.applications);

  const handleApplicationSelect = useCallback(
    ({ implementation, name }: AddApplicationSelection) => {
      dispatch(selectApplicationSchema({ implementation, presetName: name }));
      onApplicationSelect();
    },
    [dispatch, onApplicationSelect]
  );

  useEffect(() => {
    dispatch(fetchApplicationSchema());
  }, [dispatch]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddApplication')}</ModalHeader>

      <ModalBody>
        {isSchemaFetching ? <LoadingIndicator /> : null}

        {!isSchemaFetching && !!schemaError ? (
          <Alert kind={kinds.DANGER}>
            {translate('UnableToAddANewApplicationPleaseTryAgain')}
          </Alert>
        ) : null}

        {isSchemaPopulated && !schemaError ? (
          <div>
            <div className={styles.applications}>
              {schema.map((application) => {
                return (
                  <AddApplicationItem
                    key={application.implementation}
                    {...application}
                    implementation={application.implementation}
                    onApplicationSelect={handleApplicationSelect}
                  />
                );
              })}
            </div>
          </div>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddApplicationModalContent;
