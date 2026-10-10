import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';

// Path mappings are keyed by the host of an application's server URL.
export default function useApplicationHostOptions() {
  return useSelector(
    createSelector(
      (state: AppState) => state.settings.applications.items,
      (applications) => {
        const hosts = applications.reduce<Record<string, string[]>>(
          (acc, application) => {
            const baseUrl = application.fields.find((field) => {
              return field.name === 'baseUrl';
            });

            try {
              const host = new URL(baseUrl?.value as string).hostname;
              const group = (acc[host] = acc[host] || []);

              group.push(application.name);
            } catch {
              // Not a URL yet. Nothing to offer.
            }

            return acc;
          },
          {}
        );

        return Object.keys(hosts).map((host) => {
          return {
            key: host,
            value: host,
            hint: hosts[host].join(', '),
          };
        });
      }
    )
  );
}
