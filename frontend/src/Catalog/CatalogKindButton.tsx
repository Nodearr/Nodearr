import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import { CatalogKind } from 'typings/LocalCatalog';
import translate from 'Utilities/String/translate';
import { CATALOG_KIND_LABELS } from './catalogKinds';

interface Props {
  kind: CatalogKind;
  isSelected: boolean;
  onPress: (kind: CatalogKind) => void;
}

function CatalogKindButton({ kind, isSelected, onPress }: Props) {
  const handlePress = useCallback(() => {
    onPress(kind);
  }, [kind, onPress]);

  return (
    <Button
      kind={isSelected ? kinds.PRIMARY : kinds.DEFAULT}
      onPress={handlePress}
    >
      {translate(CATALOG_KIND_LABELS[kind])}
    </Button>
  );
}

export default CatalogKindButton;
