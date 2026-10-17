import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import {
  getErrorMessage,
  Peer,
  useCreateInvite,
  useRedeemInvite,
} from 'Nodes/useNodes';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './Home.css';

interface ConnectFriendStepProps {
  number: number;
  isActive: boolean;
  trusted: Peer[];
  // Set while an invite is out, so that the page looks for the friend redeeming it.
  onWaitingChange: (isWaiting: boolean) => void;
}

function ConnectFriendStep({
  number,
  isActive,
  trusted,
  onWaitingChange,
}: ConnectFriendStepProps) {
  // A new friend is offered everything except Hidden unless this is turned off.
  const [shareAll, setShareAll] = useState(true);
  const [code, setCode] = useState('');
  const [joinShareAll, setJoinShareAll] = useState(true);

  const create = useCreateInvite();
  const redeem = useRedeemInvite();
  const { mutate: createInvite } = create;
  const { mutate: redeemInvite } = redeem;

  const handleShareAllChange = useCallback(() => {
    setShareAll((current) => !current);
  }, []);

  const handleJoinShareAllChange = useCallback(() => {
    setJoinShareAll((current) => !current);
  }, []);

  const handleCreatePress = useCallback(() => {
    createInvite(
      { validHours: 24, label: translate('HomeInviteLabel'), shareAll },
      { onSuccess: () => onWaitingChange(true) }
    );
  }, [createInvite, shareAll, onWaitingChange]);

  const handleCodeChange = useCallback(({ value }: InputChanged<string>) => {
    setCode(value);
  }, []);

  const handleJoinPress = useCallback(() => {
    redeemInvite(
      { code: code.trim(), shareAll: joinShareAll },
      { onSuccess: () => setCode('') }
    );
  }, [redeemInvite, code, joinShareAll]);

  const isDone = trusted.length > 0;

  return (
    <SetupStep
      number={number}
      title={translate('HomeStepFriendTitle')}
      isDone={isDone}
      isActive={isActive}
      summary={translate('HomeStepFriendDone', {
        names: trusted.map((peer) => peer.name).join(', '),
      })}
      doneActions={
        <Link className={styles.cardLink} to="/nodes/invites">
          {translate('HomeInviteAnother')}
        </Link>
      }
    >
      <p className={styles.stepText}>{translate('HomeStepFriendText')}</p>

      <div className={styles.split}>
        <div className={styles.panel}>
          <h3 className={styles.panelTitle}>{translate('HomeInviteTitle')}</h3>
          <p className={styles.panelText}>{translate('HomeInviteText')}</p>

          {create.data?.code ? (
            <>
              <div>{translate('HomeInviteCodeReady')}</div>
              <div className={styles.code}>{create.data.code}</div>
              <ClipboardButton value={create.data.code} />
              <div className={styles.waiting}>
                {translate('HomeInviteWaiting')}
              </div>
            </>
          ) : (
            <>
              <label className={styles.checkbox}>
                <input
                  type="checkbox"
                  checked={shareAll}
                  onChange={handleShareAllChange}
                />{' '}
                {translate('HomeInviteShareAll')}
              </label>
              <p className={styles.panelText}>
                {translate(
                  shareAll
                    ? 'HomeInviteShareAllHelp'
                    : 'HomeInviteShareNothingHelp'
                )}
              </p>

              <SpinnerButton
                kind={kinds.PRIMARY}
                isSpinning={create.isPending}
                onPress={handleCreatePress}
              >
                {translate('HomeInviteCreate')}
              </SpinnerButton>
            </>
          )}

          {create.error ? (
            <Alert className={styles.message} kind={kinds.DANGER}>
              {getErrorMessage(create.error)}
            </Alert>
          ) : null}
        </div>

        <div className={styles.panel}>
          <h3 className={styles.panelTitle}>{translate('HomeJoinTitle')}</h3>
          <p className={styles.panelText}>{translate('HomeJoinText')}</p>

          <TextInput
            name="code"
            value={code}
            placeholder="nodearr-invite:..."
            onChange={handleCodeChange}
          />

          <label className={styles.checkbox}>
            <input
              type="checkbox"
              checked={joinShareAll}
              onChange={handleJoinShareAllChange}
            />{' '}
            {translate('HomeInviteShareAll')}
          </label>

          <div className={styles.message}>
            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={redeem.isPending}
              isDisabled={!code.trim()}
              onPress={handleJoinPress}
            >
              {translate('HomeJoin')}
            </SpinnerButton>
          </div>

          {redeem.error ? (
            <Alert className={styles.message} kind={kinds.DANGER}>
              {getErrorMessage(redeem.error)}
            </Alert>
          ) : null}
        </div>
      </div>
    </SetupStep>
  );
}

export default ConnectFriendStep;
