import React, { useCallback } from 'react';
import {
  ReceivedTitle,
  useDismissReceived,
  useReceived,
} from 'Catalog/useSharing';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './Home.css';

function ReceivedCard({ received }: { received: ReceivedTitle }) {
  const { mutate: dismiss, isPending } = useDismissReceived();

  const handleDismissPress = useCallback(() => {
    dismiss(received.id);
  }, [dismiss, received.id]);

  const what = received.summary
    ? `${received.title} - ${received.summary}`
    : received.title ?? '';

  return (
    <div className={styles.received}>
      <div className={styles.receivedText}>
        <Icon name={icons.DOWNLOAD} />{' '}
        {translate('ReceivedSentYou', {
          name: received.peerName ?? '',
          title: what,
        })}
      </div>

      <div className={styles.receivedButtons}>
        <Link className={styles.cardLink} to="/catalog/peers">
          {translate('ReceivedSeeIt')}
        </Link>

        <Button isDisabled={isPending} onPress={handleDismissPress}>
          {translate('ReceivedPutAway')}
        </Button>
      </div>
    </div>
  );
}

// What friends sent, at the top of Home whether or not setup is finished: someone
// who joined through a send has not always connected an app yet.
function Received() {
  const { data: received = [] } = useReceived();

  if (!received.length) {
    return null;
  }

  return (
    <>
      {received.map((r) => (
        <ReceivedCard key={r.id} received={r} />
      ))}
    </>
  );
}

export default Received;
