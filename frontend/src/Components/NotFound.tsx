import React from 'react';
import PageContent from 'Components/Page/PageContent';
import translate from 'Utilities/String/translate';
import styles from './NotFound.css';

interface NotFoundProps {
  message?: string;
}

function NotFound(props: NotFoundProps) {
  const { message = translate('DefaultNotFoundMessage') } = props;

  return (
    <PageContent title="Page not found">
      <div className={styles.container}>
        <div className={styles.message}>{message}</div>

        <img
          className={styles.image}
          src={`${window.Nodearr.urlBase}/Content/Images/logo.svg`}
          alt=""
        />
      </div>
    </PageContent>
  );
}

export default NotFound;
