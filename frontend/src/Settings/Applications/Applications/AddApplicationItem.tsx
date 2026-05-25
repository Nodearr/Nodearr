import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import { sizes } from 'Helpers/Props';
import Application from 'typings/Application';
import translate from 'Utilities/String/translate';
import AddApplicationPresetMenuItem from './AddApplicationPresetMenuItem';
import styles from './AddApplicationItem.css';

export interface AddApplicationSelection {
  name?: string;
  implementation: string;
  implementationName: string;
}

interface AddApplicationItemProps {
  implementation: string;
  implementationName: string;
  infoLink: string;
  presets?: Application[];
  onApplicationSelect(selection: AddApplicationSelection): void;
}

function AddApplicationItem({
  implementation,
  implementationName,
  infoLink,
  presets = [],
  onApplicationSelect,
}: AddApplicationItemProps) {
  const hasPresets = presets.length > 0;

  const handleApplicationSelect = useCallback(() => {
    onApplicationSelect({ implementation, implementationName });
  }, [implementation, implementationName, onApplicationSelect]);

  return (
    <div className={styles.application}>
      <Link className={styles.underlay} onPress={handleApplicationSelect} />

      <div className={styles.overlay}>
        <div className={styles.name}>{implementationName}</div>

        <div className={styles.actions}>
          {hasPresets ? (
            <span>
              <Button size={sizes.SMALL} onPress={handleApplicationSelect}>
                {translate('Custom')}
              </Button>

              <Menu className={styles.presetsMenu}>
                <Button className={styles.presetsMenuButton} size={sizes.SMALL}>
                  {translate('Presets')}
                </Button>

                <MenuContent>
                  {presets.map((preset) => {
                    return (
                      <AddApplicationPresetMenuItem
                        key={preset.name}
                        name={preset.name}
                        implementation={implementation}
                        implementationName={implementationName}
                        onPress={onApplicationSelect}
                      />
                    );
                  })}
                </MenuContent>
              </Menu>
            </span>
          ) : null}

          <Button to={infoLink} size={sizes.SMALL}>
            {translate('MoreInfo')}
          </Button>
        </div>
      </div>
    </div>
  );
}

export default AddApplicationItem;
