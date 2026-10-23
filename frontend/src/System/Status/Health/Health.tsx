import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Icon, { IconKind } from 'Components/Icon';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import HealthItemLink from './HealthItemLink';
import useHealth from './useHealth';
import styles from './Health.css';

const columns: Column[] = [
  {
    className: styles.status,
    name: 'type',
    label: '',
    isVisible: true,
  },
  {
    name: 'message',
    label: () => translate('Message'),
    isVisible: true,
  },
  {
    name: 'actions',
    label: () => translate('Actions'),
    isVisible: true,
  },
];

function Health() {
  const { data, isFetched, isFetching, isLoading } = useHealth();

  const healthIssues = !!data.length;

  return (
    <FieldSet
      legend={
        <div className={styles.legend}>
          {translate('Health')}

          {isFetching && !isFetched ? (
            <LoadingIndicator className={styles.loading} size={20} />
          ) : null}
        </div>
      }
    >
      {isLoading ? <LoadingIndicator /> : null}

      {isFetched && !healthIssues ? (
        <div className={styles.healthOk}>
          {translate('NoIssuesWithYourConfiguration')}
        </div>
      ) : null}

      {healthIssues ? (
        <>
          <Table columns={columns}>
            <TableBody>
              {data.map((item) => {
                const source = item.source;

                let kind: IconKind = kinds.WARNING;
                switch (item.type.toLowerCase()) {
                  case 'error':
                    kind = kinds.DANGER;
                    break;
                  default:
                  case 'warning':
                    kind = kinds.WARNING;
                    break;
                  case 'notice':
                    kind = kinds.INFO;
                    break;
                }

                return (
                  <TableRow key={`health${item.message}`}>
                    <TableRowCell>
                      <Icon
                        name={icons.DANGER}
                        kind={kind}
                        title={titleCase(item.type)}
                      />
                    </TableRowCell>

                    <TableRowCell>{item.message}</TableRowCell>

                    <TableRowCell>
                      <HealthItemLink source={source} />
                    </TableRowCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>

          <Alert kind={kinds.INFO}>
            <InlineMarkdown
              data={translate('HealthMessagesInfoBox', {
                link: '/system/logs/files',
              })}
            />
          </Alert>
        </>
      ) : null}
    </FieldSet>
  );
}

export default Health;
