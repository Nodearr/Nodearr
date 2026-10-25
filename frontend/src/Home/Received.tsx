import React, { useCallback } from 'react';
import GetItForm from 'Catalog/GetIt/GetItForm';
import {
  GetItOptions,
  ReceivedTitle,
  useDismissReceived,
  useGetIt,
  useGetItPlan,
  useReceived,
} from 'Catalog/useSharing';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { icons, kinds } from 'Helpers/Props';
import { getErrorMessage } from 'Nodes/useNodes';
import translate from 'Utilities/String/translate';
import Poster, { peerPosterSrc } from '../Catalog/Poster';
import getItStyles from '../Catalog/GetIt/GetItForm.css';
import styles from './Home.css';

function GetItSection({ received }: { received: ReceivedTitle }) {
  const getIt = useGetIt();
  const { mutate } = getIt;
  const handleGetIt = useCallback(
    (options: GetItOptions) => {
      mutate({ id: received.id, options });
    },
    [mutate, received.id]
  );
  const isIdle =
    received.getItState === 'none' || received.getItState === 'failed';
  const { data: plan, isFetching, error } = useGetItPlan(received.id, isIdle);

  if (received.getItState === 'working') {
    return (
      <div className={getItStyles.getItNote}>
        <Icon name={icons.SPINNER} isSpinning={true} /> {received.getItMessage}
      </div>
    );
  }

  if (received.getItState === 'done') {
    return (
      <div className={getItStyles.getItDone}>
        <Icon name={icons.CHECK_CIRCLE} kind={kinds.SUCCESS} />{' '}
        {received.getItMessage}
      </div>
    );
  }

  return (
    <>
      {received.getItState === 'failed' ? (
        <div className={getItStyles.getItError}>
          {translate('GetItFailed', { message: received.getItMessage ?? '' })}
        </div>
      ) : null}

      {isFetching && !plan ? <LoadingIndicator size={20} /> : null}

      {error ? (
        <div className={getItStyles.getItError}>{getErrorMessage(error)}</div>
      ) : null}

      {plan?.problem ? (
        <div className={getItStyles.getItNote}>{plan.problem}</div>
      ) : null}

      {plan && !plan.problem ? (
        <GetItForm
          mediaKind={received.mediaKind}
          targets={plan.targets}
          isPending={getIt.isPending}
          error={getIt.error}
          onGetIt={handleGetIt}
        />
      ) : null}
    </>
  );
}

function ReceivedCard({ received }: { received: ReceivedTitle }) {
  const { mutate: dismiss, isPending } = useDismissReceived();

  const handleDismissPress = useCallback(() => {
    dismiss(received.id);
  }, [dismiss, received.id]);

  const what = received.summary
    ? `${received.title} - ${received.summary}`
    : received.title ?? '';

  return (
    <div className={`${styles.received} ${styles.receivedWithPoster}`}>
      <Poster
        src={
          received.posterItemId ? peerPosterSrc(received.posterItemId) : null
        }
        title={received.title ?? ''}
        size="thumb"
        isSquare={received.mediaKind === 'music'}
      />

      <div className={styles.receivedBody}>
        <div className={styles.receivedHeader}>
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

        <GetItSection received={received} />
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
