import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import TextInput from 'Components/Form/TextInput';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import { getErrorMessage, Peer, usePeers } from 'Nodes/useNodes';
import Application from 'typings/Application';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  describeSharing,
  parseShareId,
  ShareableTitle,
  shareId,
  ShareList,
  titleId,
  useDeleteShareList,
  useHashingStatus,
  useLibraryApplications,
  useSaveShareList,
  useSetShareAll,
  useShareableTitles,
  useShareLists,
} from '../useSharing';
import SendToFriend from './SendToFriend';
import {
  ArtistChoice,
  Check,
  describeTitle,
  SeriesChoice,
  toggle,
} from './ShareChoices';
import styles from '../Catalog.css';

function ShareAllRow({
  peer,
  applications,
}: {
  peer: Peer;
  applications: Application[];
}) {
  const { mutate, isPending, error } = useSetShareAll();
  const excluded = useMemo(
    () => peer.excludedApplicationIds ?? [],
    [peer.excludedApplicationIds]
  );

  const handlePress = useCallback(() => {
    mutate({ id: peer.id, shareAll: !peer.shareAll });
  }, [mutate, peer.id, peer.shareAll]);

  const handleAppToggle = useCallback(
    (id: string) => {
      mutate({
        id: peer.id,
        shareAll: true,
        excludedApplicationIds: excluded.includes(Number(id))
          ? excluded.filter((e) => e !== Number(id))
          : [...excluded, Number(id)],
      });
    },
    [mutate, peer.id, excluded]
  );

  const sharesAny =
    peer.shareAll && applications.some((app) => !excluded.includes(app.id));

  return (
    <div className={styles.peerShare}>
      <div className={styles.peerRow}>
        <div className={styles.peerName}>{peer.name}</div>

        <Label kind={sharesAny ? kinds.SUCCESS : kinds.DEFAULT}>
          {describeSharing(peer, applications)}
        </Label>

        <SpinnerButton isSpinning={isPending} onPress={handlePress}>
          {peer.shareAll
            ? translate('SharingStopEverything')
            : translate('SharingShareEverything')}
        </SpinnerButton>
      </div>

      {peer.shareAll && applications.length > 1 ? (
        <div className={styles.peerApps}>
          <span className={styles.muted}>{translate('SharingFromApps')}</span>

          {applications.map((app) => (
            <Check
              key={app.id}
              id={String(app.id)}
              label={app.name}
              isChecked={!excluded.includes(app.id)}
              onToggle={handleAppToggle}
            />
          ))}
        </div>
      ) : null}

      {error ? (
        <div className={styles.error}>{getErrorMessage(error)}</div>
      ) : null}
    </div>
  );
}

function Hashing() {
  const { data } = useHashingStatus();

  if (!data) {
    return null;
  }

  return (
    <FieldSet legend={translate('SharingHashing')}>
      <p className={styles.muted}>{translate('SharingHashingHelpText')}</p>

      {data.shared === 0 ? (
        <div className={styles.muted}>
          {translate('SharingHashingNothingShared')}
        </div>
      ) : (
        <div>
          {translate('SharingHashingProgress', {
            hashed: data.hashed,
            shared: data.shared,
          })}
        </div>
      )}

      {data.waitingForUploads ? (
        <div className={styles.muted}>
          {translate('SharingHashingWaitingForUploads')}
        </div>
      ) : null}

      {data.current && !data.waitingForUploads ? (
        <div className={styles.muted}>
          {translate('SharingHashingCurrent', { path: data.current })}
        </div>
      ) : null}
    </FieldSet>
  );
}

function ShareListEditor({
  list,
  titles,
  peers,
}: {
  list: ShareList;
  titles: ShareableTitle[];
  peers: Peer[];
}) {
  const [name, setName] = useState(list.name);
  const [filter, setFilter] = useState('');
  const [items, setItems] = useState(new Set(list.items.map(shareId)));
  const [peerIds, setPeerIds] = useState(
    new Set(list.peerIds.map((id) => `${id}`))
  );

  // Another tab, or this one after a save, may have changed the list.
  useEffect(() => {
    setName(list.name);
    setItems(new Set(list.items.map(shareId)));
    setPeerIds(new Set(list.peerIds.map((id) => `${id}`)));
  }, [list]);

  const save = useSaveShareList();
  const { mutate: saveList } = save;
  const { mutate: deleteList, error: deleteError } = useDeleteShareList();
  const saveError = save.error ?? deleteError;

  const handleNameChange = useCallback(({ value }: InputChanged<string>) => {
    setName(value);
  }, []);

  const handleFilterChange = useCallback(({ value }: InputChanged<string>) => {
    setFilter(value);
  }, []);

  const handleItemToggle = useCallback((id: string) => {
    setItems((current) => toggle(current, id));
  }, []);

  const handlePeerToggle = useCallback((id: string) => {
    setPeerIds((current) => toggle(current, id));
  }, []);

  const handleSavePress = useCallback(() => {
    saveList({
      id: list.id,
      name,
      items: Array.from(items, parseShareId),
      peerIds: Array.from(peerIds, Number),
    });
  }, [saveList, list.id, name, items, peerIds]);

  const handleDeletePress = useCallback(() => {
    deleteList(list.id);
  }, [deleteList, list.id]);

  const visible = useMemo(() => {
    const term = filter.trim().toLowerCase();

    return term
      ? titles.filter((t) => t.title.toLowerCase().includes(term))
      : titles;
  }, [titles, filter]);

  return (
    <FieldSet
      legend={list.isHidden ? translate('SharingHiddenList') : list.name}
    >
      {list.isHidden ? (
        <p className={styles.muted}>{translate('SharingHiddenHelpText')}</p>
      ) : null}

      <div className={styles.columns}>
        <div className={styles.column}>
          <div className={styles.columnLabel}>
            {translate('SharingTitles')} ({items.size})
          </div>

          <TextInput
            name={`filter-${list.id}`}
            value={filter}
            placeholder={translate('Filter')}
            onChange={handleFilterChange}
          />

          <div className={styles.titles}>
            {visible.map((title) => {
              if (title.mediaKind === 'tv') {
                return (
                  <SeriesChoice
                    key={titleId(title)}
                    title={title}
                    items={items}
                    label={describeTitle(title)}
                    onChange={setItems}
                  />
                );
              }

              if (title.mediaKind === 'music') {
                return (
                  <ArtistChoice
                    key={titleId(title)}
                    title={title}
                    items={items}
                    label={describeTitle(title)}
                    onChange={setItems}
                  />
                );
              }

              return (
                <Check
                  key={titleId(title)}
                  id={titleId(title)}
                  label={describeTitle(title)}
                  isChecked={items.has(titleId(title))}
                  className={styles.movieCheck}
                  onToggle={handleItemToggle}
                />
              );
            })}
          </div>
        </div>

        {list.isHidden ? null : (
          <div className={styles.column}>
            <div className={styles.columnLabel}>{translate('Name')}</div>
            <TextInput
              name={`name-${list.id}`}
              value={name}
              onChange={handleNameChange}
            />

            <div className={styles.columnLabel}>
              <br />
              {translate('SharingSharedWith')} ({peerIds.size})
            </div>

            {peers.length === 0 ? (
              <div className={styles.muted}>{translate('NoTrustedNodes')}</div>
            ) : null}

            {peers.map((peer) => (
              <Check
                key={peer.id}
                id={`${peer.id}`}
                label={peer.name}
                isChecked={peerIds.has(`${peer.id}`)}
                onToggle={handlePeerToggle}
              />
            ))}
          </div>
        )}
      </div>

      {saveError ? (
        <Alert kind={kinds.DANGER}>
          {translate('SharingSaveError')}: {getErrorMessage(saveError)}
        </Alert>
      ) : null}

      <div className={styles.buttons}>
        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={save.isPending}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerButton>

        {list.isHidden ? null : (
          <Button kind={kinds.DANGER} onPress={handleDeletePress}>
            {translate('Delete')}
          </Button>
        )}
      </div>
    </FieldSet>
  );
}

function Sharing() {
  const [newName, setNewName] = useState('');

  const { data: lists = [], isLoading, error } = useShareLists();
  const { data: titles = [] } = useShareableTitles();
  const { data: allPeers = [] } = usePeers();
  const applications = useLibraryApplications();
  const {
    mutate: createList,
    isPending: isCreating,
    error: createError,
  } = useSaveShareList();

  const peers = useMemo(
    () => allPeers.filter((p) => p.state === 'trusted'),
    [allPeers]
  );

  const handleNewNameChange = useCallback(({ value }: InputChanged<string>) => {
    setNewName(value);
  }, []);

  const handleCreatePress = useCallback(() => {
    createList(
      { name: newName.trim(), items: [], peerIds: [] },
      { onSuccess: () => setNewName('') }
    );
  }, [createList, newName]);

  return (
    <PageContent title={translate('Sharing')}>
      <PageContentBody>
        <Alert kind={kinds.INFO}>{translate('SharingHelpText')}</Alert>

        {isLoading ? <LoadingIndicator /> : null}

        {error ? (
          <Alert kind={kinds.DANGER}>{translate('SharingLoadError')}</Alert>
        ) : null}

        <FieldSet legend={translate('TrustedNodes')}>
          {peers.length === 0 ? (
            <div className={styles.muted}>{translate('NoTrustedNodes')}</div>
          ) : null}

          {peers.length > 0 ? (
            <p className={styles.muted}>{translate('SharingAppsHelpText')}</p>
          ) : null}

          {peers.map((peer) => (
            <ShareAllRow
              key={peer.id}
              peer={peer}
              applications={applications}
            />
          ))}
        </FieldSet>

        <SendToFriend titles={titles} peers={peers} />

        <Hashing />

        {lists.map((list) => (
          <ShareListEditor
            key={list.id}
            list={list}
            titles={titles}
            peers={peers}
          />
        ))}

        <FieldSet legend={translate('SharingNewList')}>
          {createError ? (
            <Alert kind={kinds.DANGER}>
              {translate('SharingSaveError')}: {getErrorMessage(createError)}
            </Alert>
          ) : null}

          <div className={styles.newList}>
            <div className={styles.newListName}>
              <TextInput
                name="newListName"
                value={newName}
                placeholder={translate('Name')}
                onChange={handleNewNameChange}
              />
            </div>

            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={isCreating}
              isDisabled={!newName.trim()}
              onPress={handleCreatePress}
            >
              {translate('SharingNewList')}
            </SpinnerButton>
          </div>
        </FieldSet>
      </PageContentBody>
    </PageContent>
  );
}

export default Sharing;
