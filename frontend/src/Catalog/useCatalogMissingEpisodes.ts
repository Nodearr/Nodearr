import { useCallback, useEffect, useState } from 'react';

function read(key: string, fallback: boolean) {
  try {
    const value = window.localStorage.getItem(key);
    return value === 'true' || (value !== 'false' && fallback);
  } catch {
    return fallback;
  }
}

// Tables and cards share one choice per catalog, independently of the other catalog.
export default function useCatalogMissingEpisodes(
  peer: boolean,
  deferNotification = false
) {
  const key = `nodearr.${peer ? 'peer' : 'local'}Catalog.showMissingEpisodes`;
  const [showMissing, setShowMissing] = useState(() => read(key, peer));

  useEffect(() => {
    const changed = () => setShowMissing(read(key, peer));
    window.addEventListener('storage', changed);
    window.addEventListener('catalogMissingEpisodesChanged', changed);
    return () => {
      window.removeEventListener('storage', changed);
      window.removeEventListener('catalogMissingEpisodesChanged', changed);
      if (deferNotification) {
        window.dispatchEvent(new Event('catalogMissingEpisodesChanged'));
      }
    };
  }, [key, peer, deferNotification]);

  const change = useCallback(
    (value: boolean) => {
      setShowMissing(value);
      try {
        window.localStorage.setItem(key, String(value));
        // A card can hide its own table row. Notify the table after it closes so the
        // row is not removed underneath the open card. The preference is saved at once.
        if (!deferNotification) {
          window.dispatchEvent(new Event('catalogMissingEpisodesChanged'));
        }
      } catch {
        // The choice still applies for this visit when storage is unavailable.
      }
    },
    [key, deferNotification]
  );

  return [showMissing, change] as const;
}
