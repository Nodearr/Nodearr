import React, { useCallback, useState } from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import styles from './Catalog.css';

// The poster of the title a friend's catalog item belongs to. Nodearr keeps a copy, so a
// poster is fetched from the friend once. A title without one shows a plain frame. An album
// cover is square.
function Poster({
  itemId,
  title,
  size,
  isSquare = false,
}: {
  itemId: number;
  title: string;
  size: 'thumb' | 'tile';
  isSquare?: boolean;
}) {
  const [isMissing, setIsMissing] = useState(false);

  const handleError = useCallback(() => {
    setIsMissing(true);
  }, []);

  const shape = size === 'tile' ? styles.posterTile : styles.posterThumb;
  const className = isSquare ? `${shape} ${styles.posterSquare}` : shape;

  if (isMissing) {
    return (
      <div className={`${className} ${styles.posterNone}`} title={title}>
        <Icon name={icons.EPISODE_FILE} size={size === 'tile' ? 32 : 14} />
      </div>
    );
  }

  return (
    <img
      className={className}
      src={`${window.Nodearr.urlBase}/api/v5/catalog/posters/peer/${itemId}`}
      alt={title}
      title={title}
      loading="lazy"
      onError={handleError}
    />
  );
}

export default Poster;
