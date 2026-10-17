import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import ClipboardButton from 'Components/Link/ClipboardButton';
import SpinnerButton from 'Components/Link/SpinnerButton';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  getErrorMessage,
  Invite,
  useCreateInvite,
  useDeleteInvite,
  useInvites,
  useRedeemInvite,
} from '../useNodes';
import styles from '../Nodes.css';

const COLUMNS: Column[] = [
  { name: 'label', label: () => translate('InviteLabel'), isVisible: true },
  { name: 'status', label: () => translate('InviteStatus'), isVisible: true },
  { name: 'expires', label: () => translate('InviteExpires'), isVisible: true },
  { name: 'actions', label: '', isVisible: true },
];

const VALIDITY = [
  { key: 1, value: () => translate('InviteOneHour') },
  { key: 24, value: () => translate('InviteHours', { hours: 24 }) },
  { key: 168, value: () => translate('InviteDays', { days: 7 }) },
];

function InviteRow({
  invite,
  onDelete,
}: {
  invite: Invite;
  onDelete: (id: number) => void;
}) {
  const handleDeletePress = useCallback(() => {
    onDelete(invite.id);
  }, [invite.id, onDelete]);

  const isUsed = !!invite.redeemedAt;
  const isExpired = !isUsed && new Date(invite.expires) <= new Date();

  return (
    <TableRow>
      <TableRowCell>{invite.label}</TableRowCell>

      <TableRowCell>
        {isUsed ? (
          <Label kind={kinds.SUCCESS}>{translate('InviteStatusUsed')}</Label>
        ) : null}
        {isExpired ? (
          <Label kind={kinds.WARNING}>{translate('InviteStatusExpired')}</Label>
        ) : null}
        {!isUsed && !isExpired ? (
          <Label kind={kinds.INFO}>{translate('InviteStatusOpen')}</Label>
        ) : null}
        {invite.redeemedByNodeId ? (
          <div className={styles.nodeId}>{invite.redeemedByNodeId}</div>
        ) : null}
      </TableRowCell>

      <RelativeDateCell date={invite.expires} includeTime={true} />

      <TableRowCell className={styles.actions}>
        <Button kind={kinds.DANGER} onPress={handleDeletePress}>
          {translate('Delete')}
        </Button>
      </TableRowCell>
    </TableRow>
  );
}

function NodeInvites() {
  const [validHours, setValidHours] = useState(24);
  const [label, setLabel] = useState('');
  // Friends are offered everything except Hidden unless this is turned off.
  const [shareAll, setShareAll] = useState(true);
  const [code, setCode] = useState('');
  const [redeemShareAll, setRedeemShareAll] = useState(true);

  const { data: invites = [] } = useInvites();
  const create = useCreateInvite();
  const redeem = useRedeemInvite();
  const { mutate: deleteInvite } = useDeleteInvite();

  const handleValidHoursChange = useCallback(
    ({ value }: InputChanged<number | string>) => {
      setValidHours(Number(value));
    },
    []
  );

  const handleLabelChange = useCallback(({ value }: InputChanged<string>) => {
    setLabel(value);
  }, []);

  const handleCodeChange = useCallback(({ value }: InputChanged<string>) => {
    setCode(value);
  }, []);

  const { mutate: createInvite } = create;
  const { mutate: redeemInvite } = redeem;

  const handleShareAllChange = useCallback(() => {
    setShareAll((current) => !current);
  }, []);

  const handleRedeemShareAllChange = useCallback(() => {
    setRedeemShareAll((current) => !current);
  }, []);

  const handleCreatePress = useCallback(() => {
    createInvite({ validHours, label, shareAll });
  }, [createInvite, validHours, label, shareAll]);

  const handleRedeemPress = useCallback(() => {
    redeemInvite(
      { code: code.trim(), shareAll: redeemShareAll },
      { onSuccess: () => setCode('') }
    );
  }, [redeemInvite, code, redeemShareAll]);

  const handleDelete = useCallback(
    (id: number) => {
      deleteInvite(id);
    },
    [deleteInvite]
  );

  return (
    <PageContent title={translate('Invites')}>
      <PageContentBody>
        <FieldSet legend={translate('InviteCreate')}>
          <p className={styles.muted}>{translate('InviteCreateHelpText')}</p>

          <div className={styles.formRow}>
            <div className={styles.field}>
              <div className={styles.fieldLabel}>
                {translate('InviteValidFor')}
              </div>
              <SelectInput
                name="validHours"
                value={validHours}
                values={VALIDITY}
                onChange={handleValidHoursChange}
              />
            </div>

            <div className={styles.field}>
              <div className={styles.fieldLabel}>
                {translate('InviteLabel')}
              </div>
              <TextInput
                name="label"
                value={label}
                placeholder={translate('InviteLabelHelpText')}
                onChange={handleLabelChange}
              />
            </div>

            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={create.isPending}
              onPress={handleCreatePress}
            >
              {translate('InviteCreate')}
            </SpinnerButton>
          </div>

          <label className={styles.muted}>
            <input
              type="checkbox"
              checked={shareAll}
              onChange={handleShareAllChange}
            />{' '}
            {translate('InviteShareAll')}
          </label>

          {create.error ? (
            <Alert kind={kinds.DANGER}>{getErrorMessage(create.error)}</Alert>
          ) : null}

          {create.data?.code ? (
            <>
              <div className={styles.fieldLabel}>{translate('InviteCode')}</div>
              <div className={styles.code}>{create.data.code}</div>
              <ClipboardButton value={create.data.code} />
              <p className={styles.muted}>{translate('InviteCodeHelpText')}</p>
            </>
          ) : null}
        </FieldSet>

        <FieldSet legend={translate('InviteRedeem')}>
          <p className={styles.muted}>{translate('InviteRedeemHelpText')}</p>

          <div className={styles.formRow}>
            <div className={styles.field}>
              <TextInput
                name="code"
                value={code}
                placeholder="nodearr-invite:..."
                onChange={handleCodeChange}
              />
            </div>

            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={redeem.isPending}
              isDisabled={!code.trim()}
              onPress={handleRedeemPress}
            >
              {translate('InviteRedeem')}
            </SpinnerButton>
          </div>

          <label className={styles.muted}>
            <input
              type="checkbox"
              checked={redeemShareAll}
              onChange={handleRedeemShareAllChange}
            />{' '}
            {translate('InviteRedeemShareAll')}
          </label>

          {redeem.error ? (
            <Alert kind={kinds.DANGER}>{getErrorMessage(redeem.error)}</Alert>
          ) : null}

          {redeem.data ? (
            <Alert kind={kinds.SUCCESS}>
              {redeem.data.name}: {translate('PeerStateTrusted')}
            </Alert>
          ) : null}
        </FieldSet>

        <FieldSet legend={translate('Invites')}>
          {invites.length === 0 ? (
            <Alert kind={kinds.INFO}>{translate('InvitesEmpty')}</Alert>
          ) : (
            <Table columns={COLUMNS} canModifyColumns={false}>
              <TableBody>
                {invites.map((invite) => (
                  <InviteRow
                    key={invite.id}
                    invite={invite}
                    onDelete={handleDelete}
                  />
                ))}
              </TableBody>
            </Table>
          )}
        </FieldSet>
      </PageContentBody>
    </PageContent>
  );
}

export default NodeInvites;
