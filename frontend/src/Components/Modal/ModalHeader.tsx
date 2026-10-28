import React, { ForwardedRef, forwardRef, ReactNode } from 'react';
import DocumentationLink from 'Components/DocumentationLink';
import { DocumentationTopic } from 'Utilities/Documentation/documentation';
import { useModalContext } from './ModalContext';
import styles from './ModalHeader.css';

interface ModalHeaderProps extends React.HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
  helpTopic?: DocumentationTopic;
}

const ModalHeader = forwardRef(
  (
    { children, helpTopic, ...otherProps }: ModalHeaderProps,
    ref: ForwardedRef<HTMLDivElement>
  ) => {
    const { headerId } = useModalContext();

    return (
      <div
        ref={ref}
        id={headerId}
        className={styles.modalHeader}
        {...otherProps}
      >
        {helpTopic ? (
          <div className={styles.withHelp}>
            <span className={styles.title}>{children}</span>
            <DocumentationLink topic={helpTopic} />
          </div>
        ) : (
          children
        )}
      </div>
    );
  }
);

export default ModalHeader;
