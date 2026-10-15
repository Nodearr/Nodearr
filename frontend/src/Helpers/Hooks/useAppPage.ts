import { useMemo } from 'react';
import useSystemStatus from 'System/Status/useSystemStatus';
import { useTranslations } from '../../App/useTranslations';

const useAppPage = () => {
  const { isFetched: isSystemStatusFetched, error: systemStatusError } =
    useSystemStatus();

  const { isFetched: isTranslationsFetched, error: translationsError } =
    useTranslations();

  const isPopulated = isSystemStatusFetched && isTranslationsFetched;

  const { hasError, errors } = useMemo(() => {
    return {
      hasError: !!(systemStatusError || translationsError),
      errors: {
        systemStatusError,
        translationsError,
      },
    };
  }, [systemStatusError, translationsError]);

  const isLocalStorageSupported = useMemo(() => {
    const key = 'nodearrTest';

    try {
      localStorage.setItem(key, key);
      localStorage.removeItem(key);

      return true;
    } catch {
      return false;
    }
  }, []);

  return useMemo(() => {
    return { errors, hasError, isLocalStorageSupported, isPopulated };
  }, [errors, hasError, isLocalStorageSupported, isPopulated]);
};

export default useAppPage;
