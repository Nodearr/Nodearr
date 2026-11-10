import React from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './UsageReportModal.css';

interface UsageReport {
  sending: boolean;
  report: string;
}

interface UsageReportModalProps {
  isOpen: boolean;
  onModalClose: () => void;
}

function UsageReportContent({ onModalClose }: { onModalClose: () => void }) {
  // Asked afresh each time it's opened, so it shows what would be sent now.
  const { data, isFetching, isFetched, error } = useApiQuery<UsageReport>({
    path: '/usage',
    queryOptions: { gcTime: 0, staleTime: 0 },
  });

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('UsageReport')}</ModalHeader>

      <ModalBody>
        {isFetching && !isFetched ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('UsageReportLoadError')}</Alert>
        ) : null}

        {data ? (
          <>
            <Alert kind={data.sending ? kinds.INFO : kinds.WARNING}>
              {data.sending
                ? translate('UsageReportIntro')
                : translate('UsageReportNotSending')}
            </Alert>

            <pre className={styles.report}>{data.report}</pre>
          </>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

function UsageReportModal({ isOpen, onModalClose }: UsageReportModalProps) {
  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      {isOpen ? <UsageReportContent onModalClose={onModalClose} /> : null}
    </Modal>
  );
}

export default UsageReportModal;
