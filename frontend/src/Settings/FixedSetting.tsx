import React, { ReactNode } from 'react';
import translate from 'Utilities/String/translate';
import styles from './FixedSetting.css';

interface FixedSettingProps {
  value: ReactNode;
}

// A setting given in the environment cannot be changed here. It is shown, and says so.
function FixedSetting({ value }: FixedSettingProps) {
  return (
    <div className={styles.fixed}>
      <div className={styles.fixedValue}>{value}</div>
      <div className={styles.fixedNote}>
        {translate('NodeSettingsFixedByEnvironment')}
      </div>
    </div>
  );
}

export default FixedSetting;
