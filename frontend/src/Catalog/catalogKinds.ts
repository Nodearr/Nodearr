import { CatalogKind, LocalCatalogSummary } from 'typings/LocalCatalog';

export const CATALOG_KIND_LABELS: Record<CatalogKind, string> = {
  series: 'CatalogKindSeries',
  movies: 'CatalogKindMovies',
  music: 'CatalogKindMusic',
  scenes: 'CatalogKindScenes',
};

export function getAvailableCatalogKinds(
  applications: LocalCatalogSummary[]
): CatalogKind[] {
  const implementations = applications.map((application) =>
    application.implementation?.toLowerCase()
  );
  const available: CatalogKind[] = [];

  if (implementations.some((name) => name?.startsWith('sonarr'))) {
    available.push('series');
  }
  if (
    implementations.some(
      (name) => name?.startsWith('radarr') || name?.startsWith('whisparr')
    )
  ) {
    available.push('movies');
  }
  if (implementations.some((name) => name?.startsWith('lidarr'))) {
    available.push('music');
  }
  if (implementations.some((name) => name?.startsWith('whisparr'))) {
    available.push('scenes');
  }

  return available;
}
