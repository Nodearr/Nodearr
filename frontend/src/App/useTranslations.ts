import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { setTranslations } from 'Utilities/String/translate';

interface TranslationsResponse {
  strings: Record<string, string>;
}

export function useTranslations() {
  const { data, ...result } = useApiQuery<TranslationsResponse>({
    path: '/localization',
    queryOptions: {
      staleTime: Infinity,
      gcTime: Infinity,
    },
  });

  // Set while rendering, not in an effect: the page renders as soon as these are fetched, and an
  // effect would run after that first render, so a page that never renders again, such as the
  // one for a page that does not exist, kept showing the keys instead of the words.
  if (data) {
    setTranslations(data.strings);
  }

  return {
    ...result,
    data,
  };
}
