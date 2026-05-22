import React from 'react';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './ErrorPage.css';

interface ErrorPageProps {
  version: string;
  isLocalStorageSupported: boolean;
  translationsError: ApiError | null;
  systemStatusError: ApiError | null;
}

function ErrorPage(props: ErrorPageProps) {
  const {
    version,
    isLocalStorageSupported,
    translationsError,
    systemStatusError,
  } = props;

  let errorMessage = translate('FailedToLoadNodearr');

  if (!isLocalStorageSupported) {
    errorMessage = translate('LocalStorageIsNotSupported');
  } else if (translationsError) {
    errorMessage = getErrorMessage(
      translationsError,
      translate('FailedToLoadTranslationsFromApi')
    );
  } else if (systemStatusError) {
    errorMessage = getErrorMessage(
      systemStatusError,
      translate('FailedToLoadSystemStatusFromApi')
    );
  }

  return (
    <div className={styles.page}>
      <div>{errorMessage}</div>

      <div className={styles.version}>
        {translate('VersionNumber', { version })}
      </div>
    </div>
  );
}

export default ErrorPage;
