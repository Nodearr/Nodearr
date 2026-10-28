import React from 'react';
import Scroller, { OnScroll } from 'Components/Scroller/Scroller';
import { ScrollDirection } from 'Helpers/Props/scrollDirections';
import styles from './ModalBody.css';

interface ModalBodyProps {
  className?: string;
  innerClassName?: string;
  children?: React.ReactNode;
  scrollDirection?: ScrollDirection;
  scrollTop?: number;
  onScroll?: (payload: OnScroll) => void;
}

function ModalBody({
  innerClassName = styles.innerModalBody,
  scrollDirection = 'vertical',
  scrollTop = 0,
  children,
  ...otherProps
}: ModalBodyProps) {
  let className = otherProps.className;
  const hasScroller = scrollDirection !== 'none';

  if (!className) {
    className = hasScroller ? styles.modalScroller : styles.modalBody;
  }

  return (
    <Scroller
      {...otherProps}
      className={className}
      scrollDirection={scrollDirection}
      scrollTop={scrollTop}
    >
      {hasScroller ? (
        <div className={innerClassName}>{children}</div>
      ) : (
        children
      )}
    </Scroller>
  );
}

export default ModalBody;
