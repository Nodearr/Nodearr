import React from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import styles from './Home.css';

interface SetupStepProps {
  number: number;
  title: string;
  isDone: boolean;
  isActive: boolean;
  // What a finished step says in place of its body, beside its title.
  summary?: React.ReactNode;
  // Shown beside the summary of a finished step, such as a button to change it.
  doneActions?: React.ReactNode;
  children: React.ReactNode;
}

function stepClassName(isDone: boolean, isActive: boolean) {
  if (isDone) {
    return styles.stepDone;
  }

  return isActive ? styles.stepActive : styles.step;
}

function SetupStep({
  number,
  title,
  isDone,
  isActive,
  summary,
  doneActions,
  children,
}: SetupStepProps) {
  return (
    <section className={stepClassName(isDone, isActive)}>
      <div className={styles.stepHeader}>
        <div className={isDone ? styles.stepNumberDone : styles.stepNumber}>
          {isDone ? <Icon name={icons.CHECK} /> : number}
        </div>

        <h2 className={styles.stepTitle}>
          {title}
          {isDone && summary ? (
            <span className={styles.stepSummary}> &middot; {summary}</span>
          ) : null}
        </h2>

        {isDone ? doneActions : null}
      </div>

      {isDone ? null : <div className={styles.stepBody}>{children}</div>}
    </section>
  );
}

export default SetupStep;
