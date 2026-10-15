import React, { useCallback } from 'react';
import MenuItem, { MenuItemProps } from 'Components/Menu/MenuItem';
import { AddApplicationSelection } from './AddApplicationItem';

interface AddApplicationPresetMenuItemProps
  extends Omit<MenuItemProps, 'children' | 'onPress'> {
  name: string;
  implementation: string;
  implementationName: string;
  onPress(selection: AddApplicationSelection): void;
}

function AddApplicationPresetMenuItem({
  name,
  implementation,
  implementationName,
  onPress,
  ...otherProps
}: AddApplicationPresetMenuItemProps) {
  const handlePress = useCallback(() => {
    onPress({
      name,
      implementation,
      implementationName,
    });
  }, [implementation, implementationName, name, onPress]);

  return (
    <MenuItem {...otherProps} onPress={handlePress}>
      {name}
    </MenuItem>
  );
}

export default AddApplicationPresetMenuItem;
