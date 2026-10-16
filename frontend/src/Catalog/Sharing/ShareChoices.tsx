import React, { useCallback, useEffect, useRef, useState } from 'react';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import {
  albumShareId,
  episodeShareId,
  seasonShareId,
  ShareableSeason,
  ShareableTitle,
  titleId,
  useShareableAlbums,
  useShareableSeasons,
} from '../useSharing';
import styles from '../Catalog.css';

// Choosing a title, or parts of it, the way the Arr apps monitor them. Used for share
// lists and for sending something to a friend.

export function Check({
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
export function SeriesChoice({
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

// An artist, whole, or some of its albums, as a series is shared whole or by season.
export function ArtistChoice({
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
  const { data: albums = [], isFetching } = useShareableAlbums(title, isOpen);

  const whole = titleId(title);
  const isWhole = items.has(whole);
  const isPart =
    !isWhole && Array.from(items).some((i) => i.startsWith(`${whole}:`));

  const handleArtistToggle = useCallback(() => {
    const next = new Set(
      Array.from(items).filter((i) => i !== whole && !i.startsWith(`${whole}:`))
    );

    if (!isWhole) {
      next.add(whole);
    }

    onChange(next);
  }, [items, whole, isWhole, onChange]);

  const handleAlbumToggle = useCallback(
    (id: string) => {
      const next = new Set(items);

      // Taking one album off a whole artist leaves every other album ticked.
      if (isWhole) {
        next.delete(whole);
        albums
          .map((a) => albumShareId(title, a.albumArrId))
          .filter((a) => a !== id)
          .forEach((a) => next.add(a));
      } else if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }

      onChange(next);
    },
    [items, albums, title, whole, isWhole, onChange]
  );

  const handleOpenPress = useCallback(() => {
    setIsOpen((open) => !open);
  }, []);

  return (
    <div>
      <div className={styles.seriesRow}>
        <IconButton
          name={isOpen ? icons.COLLAPSE : icons.EXPAND}
          title={translate('SharingAlbums')}
          onPress={handleOpenPress}
        />

        <Check
          id={whole}
          label={label}
          isChecked={isWhole}
          isIndeterminate={isPart}
          className={styles.inlineCheck}
          onToggle={handleArtistToggle}
        />
      </div>

      {isOpen ? (
        <div className={styles.nested}>
          {isFetching && !albums.length ? <LoadingIndicator size={20} /> : null}

          {isPart ? (
            <div className={styles.muted}>
              {translate('SharingPartOfArtistHelpText')}
            </div>
          ) : null}

          {albums.map((album) => {
            const id = albumShareId(title, album.albumArrId);
            const name = album.year
              ? `${album.title ?? ''} (${album.year})`
              : album.title ?? '';

            return (
              <Check
                key={album.albumArrId}
                id={id}
                label={
                  album.isWhole
                    ? name
                    : `${name} (${translate('SharingAlbumNotWhole')})`
                }
                isChecked={isWhole || items.has(id)}
                onToggle={handleAlbumToggle}
              />
            );
          })}
        </div>
      ) : null}
    </div>
  );
}

export function describeTitle(title: ShareableTitle) {
  const name = title.year ? `${title.title} (${title.year})` : title.title;
  // An artist's files are its albums' tracks.
  const isMusic = title.mediaKind === 'music';
  const one = isMusic ? 'SharingOneTrack' : 'SharingOneFile';
  const many = isMusic ? 'SharingTrackCount' : 'SharingFileCount';
  const files =
    title.files === 1
      ? translate(one)
      : translate(many, { count: title.files });

  return `${name} - ${files}`;
}

export function toggle(set: Set<string>, id: string) {
  const next = new Set(set);

  if (next.has(id)) {
    next.delete(id);
  } else {
    next.add(id);
  }

  return next;
}
