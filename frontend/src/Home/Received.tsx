import React, { useCallback, useEffect, useRef } from 'react';
import CatalogDetailsButton from 'Catalog/Details/CatalogDetailsButton';
import GetItForm from 'Catalog/GetIt/GetItForm';
import {
  GetItOptions,
  ReceivedTitle,
  useDismissReceived,
  useGetIt,
  useGetItPlan,
  useMarkReceivedSeen,
  useReceived,
} from 'Catalog/useSharing';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
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
    (received.getItState === 'none' || received.getItState === 'failed') &&
    !received.alreadyHaveIt;
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

  if (received.alreadyHaveIt) {
    return (
      <div className={getItStyles.getItDone}>
        <Icon name={icons.CHECK_CIRCLE} kind={kinds.SUCCESS} />{' '}
        {translate('ReceivedAlreadyHaveIt')}
      </div>
    );
  }

  return (
    <>
      {received.newFiles == null ? null : (
        <div className={getItStyles.getItNote}>
          {translate('ReceivedNewFiles', { count: received.newFiles })}
          {received.upgradeFiles ? (
            <>
              {' '}
              {translate('ReceivedUpgradeFiles', {
                count: received.upgradeFiles,
              })}
            </>
          ) : null}
        </div>
      )}

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
  const { mutate: markSeen } = useMarkReceivedSeen();
  const card = useRef<HTMLDivElement>(null);
  const shouldMarkSeen =
    received.alreadyHaveIt &&
    !received.alreadyHeldSeenAt &&
    (received.getItState === 'none' || received.getItState === 'failed');

  useEffect(() => {
    if (!shouldMarkSeen || !card.current) {
      return;
    }

    let visible = false;
    let marked = false;
    const seen = () => {
      if (visible && document.visibilityState === 'visible' && !marked) {
        marked = true;
        markSeen(received.id);
      }
    };
    const observer = new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      seen();
    });
    observer.observe(card.current);
    document.addEventListener('visibilitychange', seen);

    return () => {
      observer.disconnect();
      document.removeEventListener('visibilitychange', seen);
    };
  }, [shouldMarkSeen, received.id, markSeen]);

  const handleDismissPress = useCallback(() => {
    dismiss(received.id);
  }, [dismiss, received.id]);

  const what = received.summary
    ? `${received.title} - ${received.summary}`
    : received.title ?? '';

  return (
    <div
      ref={card}
      data-received-id={received.id}
      className={`${styles.received} ${styles.receivedWithPoster}`}
    >
      {received.detailsAvailable ? (
        <CatalogDetailsButton
          path={`/sharing/sends/received/${received.id}/details`}
          label={what}
          variant="poster"
        >
          <Poster
            src={
              received.posterItemId
                ? peerPosterSrc(received.posterItemId)
                : null
            }
            title={received.title ?? ''}
            size="thumb"
            isSquare={received.mediaKind === 'music'}
          />
        </CatalogDetailsButton>
      ) : (
        <Poster
          src={
            received.posterItemId ? peerPosterSrc(received.posterItemId) : null
          }
          title={received.title ?? ''}
          size="thumb"
          isSquare={received.mediaKind === 'music'}
        />
      )}

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
            {received.detailsAvailable ? (
              <CatalogDetailsButton
                path={`/sharing/sends/received/${received.id}/details`}
                label={what}
                variant="title"
              >
                {translate('Details')}
              </CatalogDetailsButton>
            ) : (
              <span className={styles.cardLink}>
                {translate('ReceivedDetailsUnavailable')}
              </span>
            )}

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
