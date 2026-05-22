import { useMemo } from 'react';
import useSystemStatus from 'System/Status/useSystemStatus';

const useAppPage = () => {
  const { isFetched: isSystemStatusFetched, error: systemStatusError } =
    useSystemStatus();

  const isPopulated = isSystemStatusFetched;

  const { hasError, errors } = useMemo(() => {
    return {
      hasError: !!systemStatusError,
      errors: {
        systemStatusError,
      },
    };
  }, [systemStatusError]);

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
