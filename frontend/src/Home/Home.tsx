import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useState } from 'react';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { icons, kinds } from 'Helpers/Props';
import { Peer, useNodeIdentity } from 'Nodes/useNodes';
import { NodeSettingsModel } from 'Settings/Node/useNodeSettings';
import { useSettings } from 'Settings/useSettings';
import Application from 'typings/Application';
import translate from 'Utilities/String/translate';
import ConnectAppsStep from './ConnectAppsStep';
import ConnectFriendStep from './ConnectFriendStep';
import NameNodeStep, { hasChosenName } from './NameNodeStep';
import Overview from './Overview';
import Received from './Received';
import styles from './Home.css';

const APPLICATIONS_PATH = '/applications';
const PEERS_PATH = '/nodes/peers';
const ALL_SET_DISMISSED = 'nodearr.home.allSetDismissed';

function readDismissed() {
  try {
    return window.localStorage.getItem(ALL_SET_DISMISSED) === 'true';
  } catch {
    return false;
  }
}

function Home() {
  const queryClient = useQueryClient();
  const [isWaitingForFriend, setIsWaitingForFriend] = useState(false);
  const [isAllSetDismissed, setIsAllSetDismissed] = useState(readDismissed);

  const { data: identity } = useNodeIdentity();
  const { data: nodeSettings, isFetched: isNodeSettingsFetched } =
    useSettings<NodeSettingsModel>('/settings/node');
  const { data: allApplications = [], isFetched: isApplicationsFetched } =
    useApiQuery<Application[]>({ path: APPLICATIONS_PATH });

  // The apps whose libraries Nodearr reads. Prowlarr only searches, and holds no library.
  const applications = allApplications.filter(
    (app) => app.implementation !== 'Prowlarr'
  );

  // While an invite is out, the page looks every few seconds for the friend redeeming it.
  const { data: peers = [], isFetched: isPeersFetched } = useApiQuery<Peer[]>({
    path: PEERS_PATH,
    queryOptions: { refetchInterval: isWaitingForFriend ? 3000 : false },
  });

  const handleApplicationsChanged = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: [APPLICATIONS_PATH] });
  }, [queryClient]);

  const handleDismissAllSet = useCallback(() => {
    setIsAllSetDismissed(true);

    try {
      window.localStorage.setItem(ALL_SET_DISMISSED, 'true');
    } catch {
      // Shown again next time. Nothing is lost.
    }
  }, []);

  const trusted = peers.filter((peer) => peer.state === 'trusted');
  const hasFriend = trusted.length > 0;

  useEffect(() => {
    if (hasFriend) {
      setIsWaitingForFriend(false);
    }
  }, [hasFriend]);

  const isLoaded =
    isApplicationsFetched && isPeersFetched && isNodeSettingsFetched;

  const steps = [
    applications.length > 0,
    hasChosenName(nodeSettings),
    hasFriend,
  ];
  const doneCount = steps.filter(Boolean).length;
  const activeStep = steps.indexOf(false);
  const isSetUp = doneCount === steps.length;

  return (
    <PageContent title={translate('Home')}>
      <PageContentBody>
        <div className={styles.page}>
          {isLoaded ? null : <LoadingIndicator />}

          {isLoaded ? <Received /> : null}

          {isLoaded && !isSetUp ? (
            <>
              <div className={styles.hero}>
                <h1 className={styles.heroTitle}>
                  {translate('HomeWelcomeTitle')}
                </h1>
                <p className={styles.heroText}>
                  {translate('HomeWelcomeText')}
                </p>
                <div className={styles.progress}>
                  {translate('HomeSetupProgress', {
                    done: doneCount,
                    total: steps.length,
                  })}
                </div>
              </div>

              <ConnectAppsStep
                number={1}
                isActive={activeStep === 0}
                applications={applications}
                onApplicationsChanged={handleApplicationsChanged}
              />

              <NameNodeStep number={2} isActive={activeStep === 1} />

              <ConnectFriendStep
                number={3}
                isActive={activeStep === 2}
                trusted={trusted}
                onWaitingChange={setIsWaitingForFriend}
              />
            </>
          ) : null}

          {isLoaded && isSetUp ? (
            <>
              {isAllSetDismissed ? null : (
                <div className={styles.allSet}>
                  <IconButton
                    className={styles.dismiss}
                    name={icons.CLOSE}
                    title={translate('HomeDismiss')}
                    onPress={handleDismissAllSet}
                  />
                  <h1 className={styles.heroTitle}>
                    <Icon name={icons.CHECK_CIRCLE} kind={kinds.SUCCESS} />{' '}
                    {translate('HomeAllSetTitle')}
                  </h1>
                  <p className={styles.heroText}>
                    {translate('HomeAllSetText')}
                  </p>
                </div>
              )}

              <Overview
                identity={identity}
                trusted={trusted}
                applications={applications}
              />
            </>
          ) : null}
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default Home;
