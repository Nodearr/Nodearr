import type { GetItOptions, GetItTarget } from 'Catalog/useSharing';

// Keep the person's choices while the plan changes, replacing only choices the app no longer offers.
function reconcileGetItOptions(
  current: GetItOptions | undefined,
  targets: GetItTarget[]
): GetItOptions | undefined {
  const target =
    targets.find((t) => t.applicationId === current?.applicationId) ??
    targets.find((t) => t.defaults);

  if (!target) {
    return undefined;
  }

  if (current?.applicationId !== target.applicationId) {
    return target.defaults;
  }

  // An app that temporarily does not answer has no defaults. Keep the choices until it answers.
  if (!target.defaults) {
    return current;
  }

  return {
    ...current,
    rootFolderPath:
      current.rootFolderPath &&
      target.rootFolders.some((f) => f.path === current.rootFolderPath)
        ? current.rootFolderPath
        : target.defaults.rootFolderPath,
    qualityProfileId: target.qualityProfiles.some(
      (p) => p.id === current.qualityProfileId
    )
      ? current.qualityProfileId
      : target.defaults.qualityProfileId,
    metadataProfileId: target.metadataProfiles.some(
      (p) => p.id === current.metadataProfileId
    )
      ? current.metadataProfileId
      : target.defaults.metadataProfileId,
  };
}

export default reconcileGetItOptions;
