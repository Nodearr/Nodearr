import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { GetItOptions, GetItTarget, MediaKind } from 'Catalog/useSharing';
import SelectInput from 'Components/Form/SelectInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import reconcileGetItOptions from 'Home/reconcileGetItOptions';
import { getErrorMessage } from 'Nodes/useNodes';
import { InputChanged } from 'typings/inputs';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './GetItForm.css';

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
  mediaKind,
  onGetIt,
  isPending,
  error,
  targets,
}: {
  mediaKind: MediaKind;
  onGetIt: (options: GetItOptions) => void;
  isPending: boolean;
  error?: ApiError | null;
  targets: GetItTarget[];
}) {
  const [selection, setSelection] = useState<GetItOptions | undefined>(
    targets.find((t) => t.defaults)?.defaults
  );
  const options = reconcileGetItOptions(selection, targets);

  const target =
    targets.find((t) => t.applicationId === options?.applicationId) ??
    targets[0];

  // Plans can change after a root folder or profile is added or removed in the Arr app.
  useEffect(() => {
    setSelection((current) => reconcileGetItOptions(current, targets));
  }, [targets]);

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
      setSelection(
        targets.find((t) => t.applicationId === Number(value))?.defaults ?? {
          applicationId: Number(value),
          qualityProfileId: 0,
          seasonFolder: true,
        }
      );
    },
    [targets]
  );

  const handleChange = useCallback(({ name, value }: InputChanged<string>) => {
    setSelection((current) =>
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
      onGetIt(options);
    }
  }, [onGetIt, options]);

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

          {mediaKind === 'music' ? (
            <Choice
              label={translate('MetadataProfile')}
              name="metadataProfileId"
              value={options.metadataProfileId ?? ''}
              values={metadataProfiles}
              onChange={handleChange}
            />
          ) : null}

          {mediaKind === 'tv' ? (
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

      {error ? (
        <div className={styles.getItError}>{getErrorMessage(error)}</div>
      ) : null}

      {target.problem ? null : (
        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isPending}
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

export default GetItForm;
