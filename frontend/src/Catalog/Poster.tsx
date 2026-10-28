import React, { useCallback, useState } from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import { PosterSubject } from 'typings/LocalCatalog';
import { TitleKey } from './useSharing';
import styles from './Catalog.css';

// The poster of the title a friend's catalog item belongs to. Nodearr keeps a copy, so a
// poster is fetched from the friend once.
export function peerPosterSrc(itemId: number) {
  return `${window.Nodearr.urlBase}/api/v5/catalog/posters/peer/${itemId}`;
}

// The poster of a title in this node's own library, from its own Arr app.
export function localPosterSrc(
  applicationId: number,
  subject: PosterSubject,
  arrId: number
) {
  return `${window.Nodearr.urlBase}/api/v5/catalog/posters/local/${applicationId}/${subject}/${arrId}`;
}

// The poster of a whole title on the Sharing page, including a Lidarr artist.
export function titlePosterSrc(title: TitleKey) {
  switch (title.mediaKind) {
    case 'tv':
      return localPosterSrc(title.applicationId, 'series', title.arrId);
    case 'movie':
      return localPosterSrc(title.applicationId, 'movie', title.arrId);
    case 'music':
      return localPosterSrc(title.applicationId, 'artist', title.arrId);
    default:
      return null;
  }
}

const SHAPES = {
  mini: styles.posterMini,
  thumb: styles.posterThumb,
  tile: styles.posterTile,
};

// A title's poster. A title without one shows a plain frame. An album cover is square.
function Poster({
  src,
  title,
  size,
  isSquare = false,
}: {
  src: string | null;
  title: string;
  size: 'mini' | 'thumb' | 'tile';
  isSquare?: boolean;
}) {
  const [isMissing, setIsMissing] = useState(false);

  const handleError = useCallback(() => {
    setIsMissing(true);
  }, []);

  const shape = SHAPES[size];
  const className = isSquare ? `${shape} ${styles.posterSquare}` : shape;

  if (isMissing || !src) {
    return (
      <div className={`${className} ${styles.posterNone}`} title={title}>
        <Icon name={icons.EPISODE_FILE} size={size === 'tile' ? 32 : 14} />
      </div>
    );
  }

  return (
    <img
      className={className}
      src={src}
      alt={title}
      title={title}
      loading="lazy"
      onError={handleError}
    />
  );
}

export default Poster;
