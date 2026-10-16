import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import ClipboardButton from 'Components/Link/ClipboardButton';
import IconButton from 'Components/Link/IconButton';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { icons, kinds } from 'Helpers/Props';
import { getErrorMessage, Peer } from 'Nodes/useNodes';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  parseShareId,
  SendStatus,
  SentTitle,
  ShareableTitle,
  titleId,
  useForgetSend,
  useSends,
  useSendTitle,
} from '../useSharing';
import {
  ArtistChoice,
  Check,
  describeTitle,
  SeriesChoice,
} from './ShareChoices';
import styles from '../Catalog.css';

// Someone who is not a friend yet is sent an invite of their own.
const SOMEONE_NEW = 'new';

// Enough to find a title by typing, without listing a large library.
const MAX_MATCHES = 30;

const VALIDITY = [
  { key: 24, value: () => translate('InviteHours', { hours: 24 }) },
  { key: 72, value: () => translate('InviteDays', { days: 3 }) },
  { key: 168, value: () => translate('InviteDays', { days: 7 }) },
];

const STATUS_KINDS: Record<
  SendStatus,
  'info' | 'danger' | 'warning' | 'success'
> = {
  waitingForInvite: kinds.INFO,
  inviteLapsed: kinds.DANGER,
  notTold: kinds.WARNING,
  told: kinds.SUCCESS,
};

const STATUS_LABELS: Record<SendStatus, string> = {
  waitingForInvite: 'SendStatusWaitingForInvite',
  inviteLapsed: 'SendStatusInviteLapsed',
  notTold: 'SendStatusNotTold',
  told: 'SendStatusTold',
};

function describeSend(sent: SentTitle) {
  return sent.summary ? `${sent.title} - ${sent.summary}` : sent.title ?? '';
}

function SentRow({ sent }: { sent: SentTitle }) {
  const { mutate: forget, isPending } = useForgetSend();

  const handleForgetPress = useCallback(() => {
    forget(sent.id);
  }, [forget, sent.id]);

  return (
    <div className={styles.peerRow}>
      <div className={styles.peerName}>{describeSend(sent)}</div>

      <div>{sent.peerName ?? translate('SendSomeoneNew')}</div>

      <span title={sent.lastError}>
        <Label kind={STATUS_KINDS[sent.status]}>
          {translate(STATUS_LABELS[sent.status])}
        </Label>
      </span>

      {sent.status === 'notTold' && sent.lastError ? (
        <div className={styles.muted}>{sent.lastError}</div>
      ) : null}

      <IconButton
        name={icons.REMOVE}
        title={translate('SendForget')}
        isDisabled={isPending}
        onPress={handleForgetPress}
      />
    </div>
  );
}

function SendToFriend({
  titles,
  peers,
}: {
  titles: ShareableTitle[];
  peers: Peer[];
}) {
  const [filter, setFilter] = useState('');
  const [chosen, setChosen] = useState<ShareableTitle | null>(null);
  const [items, setItems] = useState<Set<string>>(new Set());
  // Until one is picked: the first friend, once the list of friends has loaded.
  const [recipient, setRecipient] = useState<string | null>(null);
  const [validHours, setValidHours] = useState(72);

  const { data: sends = [] } = useSends();
  const sendTitle = useSendTitle();
  const { mutate, reset } = sendTitle;

  const recipients = useMemo(
    () => [
      ...peers.map((p) => ({ key: `${p.id}`, value: p.name })),
      { key: SOMEONE_NEW, value: translate('SendSomeoneNewWithInvite') },
    ],
    [peers]
  );

  // A friend chosen before may have gone.
  const firstChoice = peers.length ? `${peers[0].id}` : SOMEONE_NEW;
  const to =
    recipient === SOMEONE_NEW || peers.some((p) => `${p.id}` === recipient)
      ? (recipient as string)
      : firstChoice;

  const matches = useMemo(() => {
    const term = filter.trim().toLowerCase();

    return (
      term ? titles.filter((t) => t.title.toLowerCase().includes(term)) : titles
    )
      .filter((t) => t.files > 0)
      .slice(0, MAX_MATCHES);
  }, [titles, filter]);

  const handleFilterChange = useCallback(({ value }: InputChanged<string>) => {
    setFilter(value);
  }, []);

  const handleChoose = useCallback(
    (id: string) => {
      const title = titles.find((t) => titleId(t) === id) ?? null;

      setChosen(title);
      setItems(new Set(title ? [titleId(title)] : []));
      reset();
    },
    [titles, reset]
  );

  const handleChooseAgainPress = useCallback(() => {
    setChosen(null);
    setItems(new Set());
  }, []);

  const handleMovieToggle = useCallback((id: string) => {
    setItems((current) => (current.has(id) ? new Set() : new Set([id])));
  }, []);

  const handleRecipientChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setRecipient(value);
      reset();
    },
    [reset]
  );

  const handleValidHoursChange = useCallback(
    ({ value }: InputChanged<number | string>) => {
      setValidHours(Number(value));
    },
    []
  );

  const handleSendPress = useCallback(() => {
    mutate(
      {
        peerId: to === SOMEONE_NEW ? undefined : Number(to),
        items: Array.from(items, parseShareId),
        validHours: to === SOMEONE_NEW ? validHours : undefined,
      },
      {
        onSuccess: () => {
          setChosen(null);
          setItems(new Set());
          setFilter('');
        },
      }
    );
  }, [mutate, to, items, validHours]);

  const sent = sendTitle.data;

  return (
    <FieldSet legend={translate('SendToFriend')}>
      <p className={styles.muted}>{translate('SendToFriendHelpText')}</p>

      <div className={styles.columns}>
        <div className={styles.column}>
          <div className={styles.columnLabel}>{translate('SendWhat')}</div>

          {chosen ? (
            <>
              <div className={styles.titles}>
                {chosen.mediaKind === 'tv' ? (
                  <SeriesChoice
                    title={chosen}
                    items={items}
                    label={describeTitle(chosen)}
                    onChange={setItems}
                  />
                ) : null}

                {chosen.mediaKind === 'music' ? (
                  <ArtistChoice
                    title={chosen}
                    items={items}
                    label={describeTitle(chosen)}
                    onChange={setItems}
                  />
                ) : null}

                {chosen.mediaKind === 'movie' ? (
                  <Check
                    id={titleId(chosen)}
                    label={describeTitle(chosen)}
                    isChecked={items.has(titleId(chosen))}
                    className={styles.movieCheck}
                    onToggle={handleMovieToggle}
                  />
                ) : null}
              </div>

              <Button onPress={handleChooseAgainPress}>
                {translate('SendChooseSomethingElse')}
              </Button>
            </>
          ) : (
            <>
              <TextInput
                name="sendFilter"
                value={filter}
                placeholder={translate('Filter')}
                onChange={handleFilterChange}
              />

              <div className={styles.titles}>
                {matches.map((title) => (
                  <Check
                    key={titleId(title)}
                    id={titleId(title)}
                    label={describeTitle(title)}
                    isChecked={false}
                    className={styles.movieCheck}
                    onToggle={handleChoose}
                  />
                ))}

                {matches.length === MAX_MATCHES ? (
                  <div className={styles.muted}>
                    {translate('SendTypeToFindMore')}
                  </div>
                ) : null}
              </div>
            </>
          )}
        </div>

        <div className={styles.column}>
          <div className={styles.columnLabel}>{translate('SendTo')}</div>
          <SelectInput
            name="recipient"
            value={to}
            values={recipients}
            onChange={handleRecipientChange}
          />

          {to === SOMEONE_NEW ? (
            <>
              <div className={styles.columnLabel}>
                <br />
                {translate('InviteValidFor')}
              </div>
              <SelectInput
                name="validHours"
                value={validHours}
                values={VALIDITY}
                onChange={handleValidHoursChange}
              />
              <p className={styles.muted}>
                {translate('SendSomeoneNewHelpText')}
              </p>
            </>
          ) : null}
        </div>
      </div>

      {sendTitle.error ? (
        <Alert kind={kinds.DANGER}>{getErrorMessage(sendTitle.error)}</Alert>
      ) : null}

      {sent?.inviteCode ? (
        <Alert kind={kinds.SUCCESS}>
          <div>
            {translate('SendInviteReady', { title: describeSend(sent) })}
          </div>
          <div className={styles.inviteCode}>{sent.inviteCode}</div>
          <ClipboardButton value={sent.inviteCode} />
        </Alert>
      ) : null}

      {sent && !sent.inviteCode ? (
        <Alert kind={kinds.SUCCESS}>
          {translate('SendDone', {
            title: describeSend(sent),
            name: sent.peerName ?? '',
          })}
        </Alert>
      ) : null}

      <div className={styles.buttons}>
        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={sendTitle.isPending}
          isDisabled={items.size === 0}
          onPress={handleSendPress}
        >
          {translate('Send')}
        </SpinnerButton>
      </div>

      {sends.length ? (
        <>
          <div className={styles.columnLabel}>{translate('SendSent')}</div>
          {sends.map((s) => (
            <SentRow key={s.id} sent={s} />
          ))}
        </>
      ) : null}
    </FieldSet>
  );
}

export default SendToFriend;
