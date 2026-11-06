import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { useAppValues } from './appStore';
import styles from './AppUpdatedModalContent.css';

interface AppUpdatedModalContentProps {
  onModalClose: () => void;
}

// Shown when the page finds Nodearr running a newer version than the page was loaded from. What
// changed is in the changelog on nodearr.com, which the browser opens: Nodearr itself never calls
// nodearr.com (ADR 0024).
function AppUpdatedModalContent(props: AppUpdatedModalContentProps) {
  const { version } = useAppValues('version');

  const { onModalClose } = props;

  const handleSeeChangesPress = useCallback(() => {
    window.open('https://nodearr.com/changelog/', '_blank', 'noopener');
  }, []);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AppUpdated')}</ModalHeader>

      <ModalBody>
        <div>
          <InlineMarkdown
            data={translate('AppUpdatedVersion', { version })}
            blockClassName={styles.version}
          />
        </div>
      </ModalBody>

      <ModalFooter>
        <Button onPress={handleSeeChangesPress}>
          {translate('RecentChanges')}
        </Button>

        <Button kind={kinds.PRIMARY} onPress={onModalClose}>
          {translate('Reload')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default AppUpdatedModalContent;
