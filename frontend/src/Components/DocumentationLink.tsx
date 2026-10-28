import { faCircleQuestion } from '@fortawesome/free-solid-svg-icons';
import React from 'react';
import IconButton from 'Components/Link/IconButton';
import {
  documentation,
  DocumentationTopic,
} from 'Utilities/Documentation/documentation';
import translate from 'Utilities/String/translate';
import styles from './DocumentationLink.css';

interface DocumentationLinkProps {
  topic: DocumentationTopic;
}

function DocumentationLink({ topic }: DocumentationLinkProps) {
  const { path, label } = documentation[topic];
  const description = translate('DocumentationHelp', {
    topic: translate(label),
  });

  return (
    <IconButton
      className={styles.link}
      name={faCircleQuestion}
      size={16}
      to={`https://nodearr.com${path}`}
      target="_blank"
      rel="noopener noreferrer"
      title={description}
      aria-label={description}
    />
  );
}

export default DocumentationLink;
