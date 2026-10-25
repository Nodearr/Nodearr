import {
  keepPreviousData,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import Command from 'Commands/Command';
import { addOrUpdateQueryClientItem } from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import { GetItOptions, GetItPlan, PeerItemStatus } from '../useSharing';

export type CatalogGetItScope = 'file' | 'season' | 'title';

export interface CatalogGetItSelection {
  itemId: number;
  scope: CatalogGetItScope;
  seasonNumber?: number;
}

interface CatalogGetItPlan {
  plan: GetItPlan;
  seasons: number[];
  files: {
    id: number;
    seasonNumber: number;
    episodeNumbers: number[];
    quality?: string;
    localStatus: PeerItemStatus;
  }[];
}

function planPath(itemId: number) {
  return `/catalog/peers/items/${itemId}/getit`;
}

export function useCatalogGetItPlan(
  selection: CatalogGetItSelection,
  enabled: boolean
) {
  return useApiQuery<CatalogGetItPlan>({
    path: planPath(selection.itemId),
    queryParams: {
      scope: selection.scope,
      seasonNumber: selection.seasonNumber,
    },
    // Follow root/profile fixes made in the Arr app while the dialog stays open.
    queryOptions: {
      enabled,
      placeholderData: keepPreviousData,
      refetchInterval: 10000,
    },
  });
}

export function useCatalogGetIt() {
  const queryClient = useQueryClient();

  return useMutation<
    Command,
    ApiError,
    CatalogGetItSelection & { options: GetItOptions }
  >({
    mutationFn: ({ itemId, ...body }) =>
      fetchJson({
        path: getQueryPath(planPath(itemId)),
        method: 'POST',
        body,
        headers: { 'X-Api-Key': window.Nodearr.apiKey },
      }),
    onSuccess: (command) => {
      queryClient.setQueryData<Command[]>(['/command'], (current = []) =>
        addOrUpdateQueryClientItem(current, command, 'id')
      );
    },
    onSettled: (_data, _error, selection) => {
      queryClient.invalidateQueries({ queryKey: [planPath(selection.itemId)] });
    },
  });
}
