import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import ReactDOM from 'react-dom';
import { useLocation } from 'react-router';
import {
  setIsSidebarVisible,
  useAppDimension,
  useAppValue,
} from 'App/appStore';
import { IconName } from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import OverlayScroller from 'Components/Scroller/OverlayScroller';
import Scroller from 'Components/Scroller/Scroller';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { icons } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import HealthStatus from 'System/Status/Health/HealthStatus';
import translate from 'Utilities/String/translate';
import Messages from './Messages/Messages';
import PageSidebarItem from './PageSidebarItem';
import styles from './PageSidebar.css';

const HEADER_HEIGHT = parseInt(dimensions.headerHeight);
const SIDEBAR_WIDTH = parseInt(dimensions.sidebarWidth);

interface SidebarItem {
  iconName?: IconName;
  title: string | (() => string);
  to: string;
  alias?: string;
  isActive?: boolean;
  isActiveParent?: boolean;
  isParentItem?: boolean;
  isChildItem?: boolean;
  statusComponent?: React.ElementType;
  children?: {
    title: string | (() => string);
    to: string;
    statusComponent?: React.ElementType;
  }[];
}

const LINKS: SidebarItem[] = [
  {
    iconName: icons.CATALOG,
    title: () => translate('Catalog'),
    to: '/',
    alias: '/catalog',
    children: [
      {
        title: () => translate('Local'),
        to: '/catalog/local',
      },
      {
        title: () => translate('PeerCatalog'),
        to: '/catalog/peers',
      },
      {
        title: () => translate('Sharing'),
        to: '/catalog/sharing',
      },
    ],
  },
  {
    iconName: icons.NODES,
    title: () => translate('Nodes'),
    to: '/nodes',
    children: [
      {
        title: () => translate('TrustedNodes'),
        to: '/nodes/trusted',
      },
      {
        title: () => translate('Invites'),
        to: '/nodes/invites',
      },
    ],
  },
  {
    iconName: icons.ACTIVITY,
    title: () => translate('Activity'),
    to: '/activity/transfers',
    children: [
      {
        title: () => translate('Transfers'),
        to: '/activity/transfers',
      },
    ],
  },
  {
    iconName: icons.SETTINGS,
    title: () => translate('Settings'),
    to: '/settings',
    children: [
      {
        title: () => translate('Apps'),
        to: '/settings/apps',
      },
      {
        title: () => translate('NodeSettingsShort'),
        to: '/settings/node',
      },
      {
        title: () => translate('General'),
        to: '/settings/general',
      },
    ],
  },
  {
    iconName: icons.SYSTEM,
    title: () => translate('System'),
    to: '/system/status',
    children: [
      {
        title: () => translate('Status'),
        to: '/system/status',
        statusComponent: HealthStatus,
      },
    ],
  },
];

function hasActiveChildLink(link: SidebarItem, pathname: string) {
  const children = link.children;

  if (!children || !children.length) {
    return false;
  }

  return children.some((child) => {
    return child.to === pathname;
  });
}

function PageSidebar() {
  const isSidebarVisible = useAppValue('isSidebarVisible');
  const isSmallScreen = useAppDimension('isSmallScreen');
  const location = useLocation();
  const sidebarRef = useRef(null);
  const touchStartX = useRef<number | null>(null);
  const touchStartY = useRef<number | null>();
  const wasSidebarVisible = usePrevious(isSidebarVisible);

  const [sidebarTransform, setSidebarTransform] = useState<{
    transition: string;
    transform: number;
  }>({
    transition: 'none',
    transform: isSidebarVisible ? 0 : SIDEBAR_WIDTH * -1,
  });

  const { pathname } = location;

  const activeParent = useMemo(() => {
    return (
      LINKS.find((link) => {
        if (link.to && link.to === pathname) {
          return true;
        }

        const children = link.children;

        if (children) {
          const matchingChild = children.find((childLink) => {
            return pathname.startsWith(childLink.to);
          });

          if (matchingChild) {
            return matchingChild;
          }
        }

        if (
          (link.to !== '/' && pathname.startsWith(link.to)) ||
          (link.alias && pathname.startsWith(link.alias))
        ) {
          return true;
        }

        return false;
      })?.to ?? LINKS[0].to
    );
  }, [pathname]);

  const handleWindowClick = useCallback(
    (event: MouseEvent) => {
      const sidebar = ReactDOM.findDOMNode(sidebarRef.current);
      const toggleButton = document.getElementById('sidebar-toggle-button');
      const target = event.target;

      if (!sidebar) {
        return;
      }

      if (
        target instanceof Node &&
        !sidebar.contains(target) &&
        !toggleButton?.contains(target) &&
        isSidebarVisible
      ) {
        event.preventDefault();
        event.stopPropagation();
        setIsSidebarVisible({ isSidebarVisible: false });
      }
    },
    [isSidebarVisible]
  );

  const handleItemPress = useCallback(() => {
    setIsSidebarVisible({ isSidebarVisible: false });
  }, []);

  const handleTouchStart = useCallback(
    (event: TouchEvent) => {
      const touches = event.touches;
      const x = touches[0].pageX;
      const y = touches[0].pageY;

      if (touches.length !== 1) {
        return;
      }

      if (isSidebarVisible && (x > 210 || x < 180)) {
        return;
      } else if (!isSidebarVisible && x > 40) {
        return;
      }

      touchStartX.current = x;
      touchStartY.current = y;
    },
    [isSidebarVisible]
  );

  const handleTouchMove = useCallback((event: TouchEvent) => {
    const touches = event.touches;
    const currentTouchX = touches[0].pageX;
    // const currentTouchY = touches[0].pageY;
    // const isSidebarVisible = this.props.isSidebarVisible;

    if (!touchStartX.current) {
      return;
    }

    if (Math.abs(touchStartX.current - currentTouchX) < 40) {
      return;
    }

    const transform = Math.min(currentTouchX - SIDEBAR_WIDTH, 0);

    setSidebarTransform({
      transition: 'none',
      transform,
    });
  }, []);

  const handleTouchEnd = useCallback(
    (event: TouchEvent) => {
      const touches = event.changedTouches;
      const currentTouch = touches[0].pageX;

      if (!touchStartX.current) {
        return;
      }

      if (currentTouch > touchStartX.current && currentTouch > 50) {
        setSidebarTransform({
          transition: 'none',
          transform: 0,
        });
      } else if (currentTouch < touchStartX.current && currentTouch < 80) {
        setSidebarTransform({
          transition: 'transform 50ms ease-in-out',
          transform: SIDEBAR_WIDTH * -1,
        });
      } else {
        setSidebarTransform({
          transition: 'none',
          transform: isSidebarVisible ? 0 : SIDEBAR_WIDTH * -1,
        });
      }

      touchStartX.current = null;
      touchStartY.current = null;
    },
    [isSidebarVisible]
  );

  const handleTouchCancel = useCallback(() => {
    touchStartX.current = null;
    touchStartY.current = null;
  }, []);

  const handleSidebarClosePress = useCallback(() => {
    setIsSidebarVisible({ isSidebarVisible: false });
  }, []);

  useEffect(() => {
    if (isSmallScreen) {
      window.addEventListener('click', handleWindowClick, { capture: true });
      window.addEventListener('touchstart', handleTouchStart);
      window.addEventListener('touchmove', handleTouchMove);
      window.addEventListener('touchend', handleTouchEnd);
      window.addEventListener('touchcancel', handleTouchCancel);
    }

    return () => {
      window.removeEventListener('click', handleWindowClick, { capture: true });
      window.removeEventListener('touchstart', handleTouchStart);
      window.removeEventListener('touchmove', handleTouchMove);
      window.removeEventListener('touchend', handleTouchEnd);
      window.removeEventListener('touchcancel', handleTouchCancel);
    };
  }, [
    isSmallScreen,
    handleWindowClick,
    handleTouchStart,
    handleTouchMove,
    handleTouchEnd,
    handleTouchCancel,
  ]);

  useEffect(() => {
    if (wasSidebarVisible !== isSidebarVisible) {
      setSidebarTransform({
        transition: 'none',
        transform: isSidebarVisible ? 0 : SIDEBAR_WIDTH * -1,
      });
    } else if (sidebarTransform.transform === 0 && !isSidebarVisible) {
      setIsSidebarVisible({ isSidebarVisible: true });
    } else if (
      sidebarTransform.transform === -SIDEBAR_WIDTH &&
      isSidebarVisible
    ) {
      setIsSidebarVisible({ isSidebarVisible: false });
    }
  }, [sidebarTransform, isSidebarVisible, wasSidebarVisible]);

  const containerStyle = useMemo(() => {
    if (!isSmallScreen) {
      return undefined;
    }

    return {
      transition: sidebarTransform.transition ?? 'none',
      transform: `translateX(${sidebarTransform.transform}px)`,
    };
  }, [isSmallScreen, sidebarTransform]);

  const ScrollerComponent = isSmallScreen ? Scroller : OverlayScroller;

  return (
    <nav
      ref={sidebarRef}
      className={styles.sidebarContainer}
      style={containerStyle}
      aria-label={translate('MainNavigation')}
    >
      {isSmallScreen ? (
        <div className={styles.sidebarHeader}>
          <div className={styles.logoContainer}>
            <Link className={styles.logoLink} to="/">
              <img
                className={styles.logo}
                src={`${window.Nodearr.urlBase}/Content/Images/logo.svg`}
                alt="Nodearr Logo"
              />
            </Link>
          </div>

          <IconButton
            className={styles.sidebarCloseButton}
            name={icons.CLOSE}
            aria-label={translate('Close')}
            size={20}
            onPress={handleSidebarClosePress}
          />
        </div>
      ) : null}

      <ScrollerComponent
        className={styles.sidebar}
        scrollDirection="vertical"
        style={{
          height: `${window.innerHeight - HEADER_HEIGHT}px`,
        }}
      >
        <div>
          {LINKS.map((link) => {
            const childWithStatusComponent = link.children?.find((child) => {
              return !!child.statusComponent;
            });

            const childStatusComponent = childWithStatusComponent
              ? childWithStatusComponent.statusComponent
              : null;

            const isActiveParent = activeParent === link.to;
            const hasActiveChild = hasActiveChildLink(link, pathname);

            return (
              <PageSidebarItem
                key={link.to}
                iconName={link.iconName}
                title={link.title}
                to={link.to}
                statusComponent={
                  isActiveParent || !childStatusComponent
                    ? link.statusComponent
                    : childStatusComponent
                }
                isActive={pathname === link.to && !hasActiveChild}
                isActiveParent={isActiveParent}
                isParentItem={!!link.children}
                onPress={handleItemPress}
              >
                {link.children &&
                  link.to === activeParent &&
                  link.children.map((child) => {
                    return (
                      <PageSidebarItem
                        key={child.to}
                        title={child.title}
                        to={child.to}
                        isActive={pathname === child.to}
                        isParentItem={false}
                        isChildItem={true}
                        statusComponent={child.statusComponent}
                        onPress={handleItemPress}
                      />
                    );
                  })}
              </PageSidebarItem>
            );
          })}
        </div>

        <Messages />
      </ScrollerComponent>
    </nav>
  );
}

export default PageSidebar;
