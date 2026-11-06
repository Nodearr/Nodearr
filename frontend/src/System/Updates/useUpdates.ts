import useApiQuery from 'Helpers/Hooks/useApiQuery';
import UpdateStatus from 'typings/UpdateStatus';

// What the last check for updates found. Checking again is the ApplicationUpdateCheck command.
const useUpdates = () => {
  return useApiQuery<UpdateStatus>({
    path: '/update',
  });
};

export default useUpdates;
