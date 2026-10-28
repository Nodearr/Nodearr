import classNames from 'classnames';
import React, { ComponentProps } from 'react';
import { sizes } from 'Helpers/Props';
import { Size } from 'Helpers/Props/sizes';
import { DocumentationTopic } from 'Utilities/Documentation/documentation';
import DocumentationLink from './DocumentationLink';
import styles from './FieldSet.css';

interface FieldSetProps {
  size?: Size;
  legend?: ComponentProps<'legend'>['children'];
  helpTopic?: DocumentationTopic;
  children?: React.ReactNode;
}

function FieldSet({
  size = sizes.MEDIUM,
  legend,
  helpTopic,
  children,
}: FieldSetProps) {
  return (
    <fieldset className={styles.fieldSet}>
      <legend
        className={classNames(
          styles.legend,
          size === sizes.SMALL && styles.small
        )}
      >
        {legend}
        {helpTopic ? <DocumentationLink topic={helpTopic} /> : null}
      </legend>
      {children}
    </fieldset>
  );
}

export default FieldSet;
