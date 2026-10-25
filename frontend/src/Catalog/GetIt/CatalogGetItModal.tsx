import React, { useCallback, useEffect, useState } from 'react';
import useCommands from 'Commands/useCommands';
import Alert from 'Components/Alert';
import SelectInput from 'Components/Form/SelectInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds } from 'Helpers/Props';
import { getErrorMessage } from 'Nodes/useNodes';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { GetItOptions, MediaKind } from '../useSharing';
import GetItForm from './GetItForm';
import {
  CatalogGetItScope,
  CatalogGetItSelection,
  useCatalogGetIt,
  useCatalogGetItPlan,
} from './useCatalogGetIt';
import styles from './GetItForm.css';

interface CatalogGetItModalProps {
  selection: CatalogGetItSelection;
  mediaKind: MediaKind;
  title: string;
  initialCommandId: number | null;
  onModalClose: () => void;
}

function CatalogGetItModal({
  selection: initialSelection,
  mediaKind,
  title,
  initialCommandId,
  onModalClose,
}: CatalogGetItModalProps) {
  const [selection, setSelection] = useState(initialSelection);
  const [commandId, setCommandId] = useState(initialCommandId);
  const { data: commands } = useCommands();
  const command = commands.find((c) => c.id === commandId);
  const isWorking =
    command?.status === 'queued' || command?.status === 'started';
  const isDone = command?.status === 'completed';
  const getIt = useCatalogGetIt();
  const { mutate } = getIt;
  const planQuery = useCatalogGetItPlan(selection, !isWorking && !isDone);
  const { data, refetch } = planQuery;
  const [choices, setChoices] = useState<typeof data>(undefined);

  useEffect(() => {
    if (data) {
      setChoices(data);
    }
  }, [data]);

  const handleGetIt = useCallback(
    (options: GetItOptions) => {
      mutate(
        { ...selection, options },
        { onSuccess: (queued) => setCommandId(queued.id) }
      );
    },
    [mutate, selection]
  );

  const handleScopeChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setSelection((current) => ({
        ...current,
        scope: value as CatalogGetItScope,
        seasonNumber: choices?.seasons[0],
      }));
    },
    [choices]
  );

  const handleSeasonChange = useCallback(({ value }: InputChanged<string>) => {
    setSelection((current) => ({ ...current, seasonNumber: Number(value) }));
  }, []);

  const handleFileChange = useCallback(({ value }: InputChanged<string>) => {
    setSelection((current) => ({ ...current, itemId: Number(value) }));
  }, []);

  const handleRetry = useCallback(() => {
    setCommandId(null);
    refetch();
  }, [refetch]);

  const canChoose = !isWorking && !isDone;
  const plan = data?.plan;

  return (
    <Modal isOpen={true} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('GetIt')} - {title}
        </ModalHeader>
        <ModalBody>
          {canChoose && mediaKind === 'tv' ? (
            <div className={styles.getItChoices}>
              <div className={styles.getItChoice}>
                <div className={styles.getItLabel}>
                  {translate('GetItSelection')}
                </div>
                <SelectInput
                  name="catalogGetItScope"
                  value={selection.scope}
                  isDisabled={!choices}
                  values={[
                    { key: 'file', value: translate('GetItEpisodeFile') },
                    { key: 'season', value: translate('Season') },
                    { key: 'title', value: translate('GetItWholeSeries') },
                  ]}
                  onChange={handleScopeChange}
                />
              </div>
              {selection.scope === 'season' ? (
                <div className={styles.getItChoice}>
                  <div className={styles.getItLabel}>{translate('Season')}</div>
                  <SelectInput
                    name="seasonNumber"
                    value={selection.seasonNumber ?? ''}
                    values={(choices?.seasons ?? []).map((n) => ({
                      key: n,
                      value: translate('SeasonNumberToken', {
                        seasonNumber: n,
                      }),
                    }))}
                    onChange={handleSeasonChange}
                  />
                </div>
              ) : null}
              {selection.scope === 'file' &&
              (choices?.files.length ?? 0) > 1 ? (
                <div className={styles.getItChoice}>
                  <div className={styles.getItLabel}>
                    {translate('GetItEpisodeFile')}
                  </div>
                  <SelectInput
                    name="itemId"
                    value={selection.itemId}
                    values={(choices?.files ?? []).map((file) => ({
                      key: file.id,
                      disabled: file.localStatus === 'have',
                      value: `S${String(file.seasonNumber).padStart(
                        2,
                        '0'
                      )}${file.episodeNumbers
                        .map((n) => `E${String(n).padStart(2, '0')}`)
                        .join('')} ${file.quality ?? ''}`,
                    }))}
                    onChange={handleFileChange}
                  />
                </div>
              ) : null}
            </div>
          ) : null}

          {planQuery.isFetching && !data ? <LoadingIndicator /> : null}
          {planQuery.error ? (
            <Alert kind={kinds.DANGER}>
              {getErrorMessage(planQuery.error)}
            </Alert>
          ) : null}
          {plan?.problem && canChoose ? (
            <Alert kind={kinds.INFO}>{plan.problem}</Alert>
          ) : null}
          {isWorking ? (
            <div className={styles.getItNote}>
              <Icon name={icons.SPINNER} isSpinning={true} />{' '}
              {command?.message || translate('GetItWorking')}
            </div>
          ) : null}
          {isDone ? (
            <div className={styles.getItDone}>
              <Icon name={icons.CHECK_CIRCLE} kind={kinds.SUCCESS} />{' '}
              {command.message}
            </div>
          ) : null}
          {command?.status === 'failed' ? (
            <Alert kind={kinds.DANGER}>
              {command.message}{' '}
              <Link onPress={handleRetry}>{translate('Retry')}</Link>
            </Alert>
          ) : null}

          {canChoose && plan && !plan.problem && !planQuery.error ? (
            <GetItForm
              mediaKind={mediaKind}
              targets={plan.targets}
              isPending={getIt.isPending || planQuery.isFetching}
              error={getIt.error}
              onGetIt={handleGetIt}
            />
          ) : null}
          <div className={styles.getItNote}>
            <Link to="/activity/transfers">{translate('GetItTransfers')}</Link>
          </div>
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}

export default CatalogGetItModal;
