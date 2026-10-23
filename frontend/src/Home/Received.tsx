import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  GetItOptions,
  GetItTarget,
  ReceivedTitle,
  useDismissReceived,
  useGetIt,
  useGetItPlan,
  useReceived,
} from 'Catalog/useSharing';
import SelectInput from 'Components/Form/SelectInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { icons, kinds } from 'Helpers/Props';
import { getErrorMessage } from 'Nodes/useNodes';
import { InputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import Poster, { peerPosterSrc } from '../Catalog/Poster';
import styles from './Home.css';

const SERIES_TYPES = [
  { key: 'standard', value: () => translate('Standard') },
  { key: 'daily', value: () => translate('Daily') },
  { key: 'anime', value: () => translate('Anime') },
];

function Choice({
  label,
  name,
  value,
  values,
  onChange,
}: {
  label: string;
  name: string;
  value: string | number;
  values: { key: string | number; value: string | (() => string) }[];
  onChange: (change: InputChanged<string>) => void;
}) {
  return (
    <div className={styles.getItChoice}>
      <div className={styles.getItLabel}>{label}</div>
      <SelectInput
        name={name}
        value={value}
        values={values}
        onChange={onChange}
      />
    </div>
  );
}

// The choices the Arr app would ask for, filled in the way most of the library already is.
function GetItForm({
  received,
  targets,
}: {
  received: ReceivedTitle;
  targets: GetItTarget[];
}) {
  const [options, setOptions] = useState<GetItOptions | undefined>(
    targets.find((t) => t.defaults)?.defaults
  );

  const target =
    targets.find((t) => t.applicationId === options?.applicationId) ??
    targets[0];

  // The plan arrives after the card is first drawn.
  useEffect(() => {
    if (!options) {
      setOptions(targets.find((t) => t.defaults)?.defaults);
    }
  }, [targets, options]);

  const getIt = useGetIt();
  const { mutate } = getIt;

  const apps = useMemo(
    () =>
      targets.map((t) => ({
        key: t.applicationId,
        value: t.applicationName ?? '',
      })),
    [targets]
  );

  const rootFolders = useMemo(
    () =>
      (target?.rootFolders ?? []).map((f) => ({
        key: f.path ?? '',
        value:
          f.freeSpace == null
            ? f.path ?? ''
            : `${f.path} (${formatBytes(f.freeSpace)} ${translate('Free')})`,
      })),
    [target]
  );

  const profiles = useMemo(
    () =>
      (target?.qualityProfiles ?? []).map((p) => ({
        key: p.id,
        value: p.name ?? '',
      })),
    [target]
  );

  const metadataProfiles = useMemo(
    () =>
      (target?.metadataProfiles ?? []).map((p) => ({
        key: p.id,
        value: p.name ?? '',
      })),
    [target]
  );

  const handleAppChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setOptions(
        targets.find((t) => t.applicationId === Number(value))?.defaults
      );
    },
    [targets]
  );

  const handleChange = useCallback(({ name, value }: InputChanged<string>) => {
    setOptions((current) =>
      current
        ? {
            ...current,
            [name]:
              name === 'qualityProfileId' || name === 'metadataProfileId'
                ? Number(value)
                : value,
          }
        : current
    );
  }, []);

  const handleGetItPress = useCallback(() => {
    if (options) {
      mutate({ id: received.id, options });
    }
  }, [mutate, received.id, options]);

  if (!target) {
    return null;
  }

  const appName = target.applicationName ?? '';

  return (
    <div className={styles.getIt}>
      {targets.length > 1 ? (
        <Choice
          label={translate('GetItApp')}
          name="applicationId"
          value={target.applicationId}
          values={apps}
          onChange={handleAppChange}
        />
      ) : null}

      {target.problem ? (
        <div className={styles.getItNote}>{target.problem}</div>
      ) : null}

      {!target.problem && target.inLibrary ? (
        <div className={styles.getItNote}>
          {translate('GetItInLibrary', { app: appName })}
        </div>
      ) : null}

      {!target.problem && !target.inLibrary && options ? (
        <div className={styles.getItChoices}>
          <Choice
            label={translate('RootFolder')}
            name="rootFolderPath"
            value={options.rootFolderPath ?? ''}
            values={rootFolders}
            onChange={handleChange}
          />

          <Choice
            label={translate('QualityProfile')}
            name="qualityProfileId"
            value={options.qualityProfileId}
            values={profiles}
            onChange={handleChange}
          />

          {received.mediaKind === 'music' ? (
            <Choice
              label={translate('MetadataProfile')}
              name="metadataProfileId"
              value={options.metadataProfileId ?? ''}
              values={metadataProfiles}
              onChange={handleChange}
            />
          ) : null}

          {received.mediaKind === 'tv' ? (
            <Choice
              label={translate('SeriesType')}
              name="seriesType"
              value={options.seriesType ?? 'standard'}
              values={SERIES_TYPES}
              onChange={handleChange}
            />
          ) : null}
        </div>
      ) : null}

      {getIt.error ? (
        <div className={styles.getItError}>{getErrorMessage(getIt.error)}</div>
      ) : null}

      {target.problem ? null : (
        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={getIt.isPending}
          isDisabled={!options}
          onPress={handleGetItPress}
        >
          {translate('GetIt')}
        </SpinnerButton>
      )}

      <div className={styles.getItNote}>
        {translate(target.inLibrary ? 'GetItHelpInLibrary' : 'GetItHelp', {
          app: appName,
        })}
      </div>
    </div>
  );
}

function GetItSection({ received }: { received: ReceivedTitle }) {
  const isIdle =
    received.getItState === 'none' || received.getItState === 'failed';
  const { data: plan, isFetching, error } = useGetItPlan(received.id, isIdle);

  if (received.getItState === 'working') {
    return (
      <div className={styles.getItNote}>
        <Icon name={icons.SPINNER} isSpinning={true} /> {received.getItMessage}
      </div>
    );
  }

  if (received.getItState === 'done') {
    return (
      <div className={styles.getItDone}>
        <Icon name={icons.CHECK_CIRCLE} kind={kinds.SUCCESS} />{' '}
        {received.getItMessage}
      </div>
    );
  }

  return (
    <>
      {received.getItState === 'failed' ? (
        <div className={styles.getItError}>
          {translate('GetItFailed', { message: received.getItMessage ?? '' })}
        </div>
      ) : null}

      {isFetching && !plan ? <LoadingIndicator size={20} /> : null}

      {error ? (
        <div className={styles.getItError}>{getErrorMessage(error)}</div>
      ) : null}

      {plan?.problem ? (
        <div className={styles.getItNote}>{plan.problem}</div>
      ) : null}

      {plan && !plan.problem ? (
        <GetItForm received={received} targets={plan.targets} />
      ) : null}
    </>
  );
}

function ReceivedCard({ received }: { received: ReceivedTitle }) {
  const { mutate: dismiss, isPending } = useDismissReceived();

  const handleDismissPress = useCallback(() => {
    dismiss(received.id);
  }, [dismiss, received.id]);

  const what = received.summary
    ? `${received.title} - ${received.summary}`
    : received.title ?? '';

  return (
    <div className={`${styles.received} ${styles.receivedWithPoster}`}>
      <Poster
        src={
          received.posterItemId ? peerPosterSrc(received.posterItemId) : null
        }
        title={received.title ?? ''}
        size="thumb"
        isSquare={received.mediaKind === 'music'}
      />

      <div className={styles.receivedBody}>
        <div className={styles.receivedHeader}>
          <div className={styles.receivedText}>
            <Icon name={icons.DOWNLOAD} />{' '}
            {translate('ReceivedSentYou', {
              name: received.peerName ?? '',
              title: what,
            })}
          </div>

          <div className={styles.receivedButtons}>
            <Link className={styles.cardLink} to="/catalog/peers">
              {translate('ReceivedSeeIt')}
            </Link>

            <Button isDisabled={isPending} onPress={handleDismissPress}>
              {translate('ReceivedPutAway')}
            </Button>
          </div>
        </div>

        <GetItSection received={received} />
      </div>
    </div>
  );
}

// What friends sent, at the top of Home whether or not setup is finished: someone
// who joined through a send has not always connected an app yet.
function Received() {
  const { data: received = [] } = useReceived();

  if (!received.length) {
    return null;
  }

  return (
    <>
      {received.map((r) => (
        <ReceivedCard key={r.id} received={r} />
      ))}
    </>
  );
}

export default Received;
