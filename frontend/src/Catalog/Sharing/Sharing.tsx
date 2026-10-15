import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import TextInput from 'Components/Form/TextInput';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { icons, kinds } from 'Helpers/Props';
import { getErrorMessage, Peer, usePeers } from 'Nodes/useNodes';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  episodeShareId,
  parseShareId,
  seasonShareId,
  ShareableSeason,
  ShareableTitle,
  shareId,
  ShareList,
  titleId,
  useDeleteShareList,
  useHashingStatus,
  useSaveShareList,
  useSetShareAll,
  useShareableSeasons,
  useShareableTitles,
  useShareLists,
} from '../useSharing';
import styles from '../Catalog.css';

function Check({
  id,
  label,
  isChecked,
  isIndeterminate = false,
  className,
  onToggle,
}: {
  id: string;
  label: string;
  isChecked: boolean;
  isIndeterminate?: boolean;
  className?: string;
  onToggle: (id: string) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  // Some of what is under it is ticked, as Sonarr shows a partly monitored season.
  useEffect(() => {
    if (inputRef.current) {
      inputRef.current.indeterminate = isIndeterminate && !isChecked;
    }
  }, [isIndeterminate, isChecked]);

  const handleChange = useCallback(() => {
    onToggle(id);
  }, [id, onToggle]);

  return (
    <label className={className ?? styles.check}>
      <input
        ref={inputRef}
        className={styles.checkBox}
        type="checkbox"
        checked={isChecked}
        onChange={handleChange}
      />
      {label}
    </label>
  );
}

function seasonLabel(seasonNumber: number) {
  return seasonNumber === 0
    ? translate('Specials')
    : translate('SeasonNumberToken', { seasonNumber });
}

// Ticking works the way Sonarr's monitoring does. The series box is the whole series,
// including seasons that arrive later. A season box is that season, including its later
// episodes. An episode box is that episode. Unticking part of something ticked whole
// keeps the rest of it ticked, one level down.
function SeriesChoice({
  title,
  items,
  label,
  onChange,
}: {
  title: ShareableTitle;
  items: Set<string>;
  label: string;
  onChange: (next: Set<string>) => void;
}) {
  const [isOpen, setIsOpen] = useState(false);
  const { data: seasons = [], isFetching } = useShareableSeasons(title, isOpen);

  const whole = titleId(title);
  const isWhole = items.has(whole);
  const isPart =
    !isWhole && Array.from(items).some((i) => i.startsWith(`${whole}:`));

  const handleSeriesToggle = useCallback(() => {
    const next = new Set(
      Array.from(items).filter((i) => i !== whole && !i.startsWith(`${whole}:`))
    );

    if (!isWhole) {
      next.add(whole);
    }

    onChange(next);
  }, [items, whole, isWhole, onChange]);

  const handleSeasonToggle = useCallback(
    (id: string) => {
      const season = seasons.find(
        (s) => seasonShareId(title, s.seasonNumber) === id
      );

      if (!season) {
        return;
      }

      const next = new Set(items);

      if (isWhole) {
        next.delete(whole);
        seasons
          .filter((s) => s !== season)
          .forEach((s) => next.add(seasonShareId(title, s.seasonNumber)));
      } else if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
        season.episodes.forEach((e) =>
          next.delete(episodeShareId(title, e.episodeArrId))
        );
      }

      onChange(next);
    },
    [items, seasons, title, whole, isWhole, onChange]
  );

  const handleEpisodeToggle = useCallback(
    (id: string) => {
      const season = seasons.find((s) =>
        s.episodes.some((e) => episodeShareId(title, e.episodeArrId) === id)
      );

      if (!season) {
        return;
      }

      const next = new Set(items);
      const seasonId = seasonShareId(title, season.seasonNumber);
      const others = season.episodes
        .map((e) => episodeShareId(title, e.episodeArrId))
        .filter((e) => e !== id);

      if (isWhole) {
        next.delete(whole);
        seasons
          .filter((s) => s !== season)
          .forEach((s) => next.add(seasonShareId(title, s.seasonNumber)));
        others.forEach((e) => next.add(e));
      } else if (next.has(seasonId)) {
        next.delete(seasonId);
        others.forEach((e) => next.add(e));
      } else if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }

      onChange(next);
    },
    [items, seasons, title, whole, isWhole, onChange]
  );

  const handleOpenPress = useCallback(() => {
    setIsOpen((open) => !open);
  }, []);

  return (
    <div>
      <div className={styles.seriesRow}>
        <IconButton
          name={isOpen ? icons.COLLAPSE : icons.EXPAND}
          title={translate('SharingSeasonsAndEpisodes')}
          onPress={handleOpenPress}
        />

        <Check
          id={whole}
          label={label}
          isChecked={isWhole}
          isIndeterminate={isPart}
          className={styles.inlineCheck}
          onToggle={handleSeriesToggle}
        />
      </div>

      {isOpen ? (
        <div className={styles.nested}>
          {isFetching && !seasons.length ? (
            <LoadingIndicator size={20} />
          ) : null}

          {isPart ? (
            <div className={styles.muted}>
              {translate('SharingPartOfSeriesHelpText')}
            </div>
          ) : null}

          {seasons.map((season) => (
            <SeasonChoice
              key={season.seasonNumber}
              title={title}
              season={season}
              items={items}
              isWhole={isWhole}
              onSeasonToggle={handleSeasonToggle}
              onEpisodeToggle={handleEpisodeToggle}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function SeasonChoice({
  title,
  season,
  items,
  isWhole,
  onSeasonToggle,
  onEpisodeToggle,
}: {
  title: ShareableTitle;
  season: ShareableSeason;
  items: Set<string>;
  isWhole: boolean;
  onSeasonToggle: (id: string) => void;
  onEpisodeToggle: (id: string) => void;
}) {
  const [isOpen, setIsOpen] = useState(false);

  const seasonId = seasonShareId(title, season.seasonNumber);
  const isTicked = isWhole || items.has(seasonId);
  const episodeIds = season.episodes.map((e) =>
    episodeShareId(title, e.episodeArrId)
  );

  const handleOpenPress = useCallback(() => {
    setIsOpen((open) => !open);
  }, []);

  return (
    <div>
      <div className={styles.seriesRow}>
        <IconButton
          name={isOpen ? icons.COLLAPSE : icons.EXPAND}
          title={translate('SharingEpisodes')}
          onPress={handleOpenPress}
        />

        <Check
          id={seasonId}
          label={seasonLabel(season.seasonNumber)}
          isChecked={isTicked}
          isIndeterminate={episodeIds.some((e) => items.has(e))}
          className={styles.inlineCheck}
          onToggle={onSeasonToggle}
        />
      </div>

      {isOpen ? (
        <div className={styles.nested}>
          {season.episodes.map((episode, index) => (
            <Check
              key={episode.episodeArrId}
              id={episodeIds[index]}
              label={`${episode.episodeNumber}. ${episode.title ?? ''}${
                episode.hasFile ? '' : ` (${translate('SharingNoFileYet')})`
              }`}
              isChecked={isTicked || items.has(episodeIds[index])}
              onToggle={onEpisodeToggle}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function ShareAllRow({ peer }: { peer: Peer }) {
  const { mutate, isPending, error } = useSetShareAll();

  const handlePress = useCallback(() => {
    mutate({ id: peer.id, shareAll: !peer.shareAll });
  }, [mutate, peer.id, peer.shareAll]);

  return (
    <div className={styles.peerRow}>
      <div className={styles.peerName}>{peer.name}</div>

      <Label kind={peer.shareAll ? kinds.SUCCESS : kinds.DEFAULT}>
        {peer.shareAll
          ? translate('SharingEverything')
          : translate('SharingListsOnly')}
      </Label>

      <SpinnerButton isSpinning={isPending} onPress={handlePress}>
        {peer.shareAll
          ? translate('SharingStopEverything')
          : translate('SharingShareEverything')}
      </SpinnerButton>

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

function describeTitle(title: ShareableTitle) {
  const name = title.year ? `${title.title} (${title.year})` : title.title;
  const files =
    title.files === 1
      ? translate('SharingOneFile')
      : translate('SharingFileCount', { count: title.files });

  return `${name} - ${files}`;
}

function toggle(set: Set<string>, id: string) {
  const next = new Set(set);

  if (next.has(id)) {
    next.delete(id);
  } else {
    next.add(id);
  }

  return next;
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
            {visible.map((title) =>
              title.mediaKind === 'tv' ? (
                <SeriesChoice
                  key={titleId(title)}
                  title={title}
                  items={items}
                  label={describeTitle(title)}
                  onChange={setItems}
                />
              ) : (
                <Check
                  key={titleId(title)}
                  id={titleId(title)}
                  label={describeTitle(title)}
                  isChecked={items.has(titleId(title))}
                  className={styles.movieCheck}
                  onToggle={handleItemToggle}
                />
              )
            )}
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

          {peers.map((peer) => (
            <ShareAllRow key={peer.id} peer={peer} />
          ))}
        </FieldSet>

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
