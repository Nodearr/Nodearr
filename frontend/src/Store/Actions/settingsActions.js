import { handleThunks } from 'Store/thunks';
import createHandleActions from './Creators/createHandleActions';
import applications from './Settings/applications';
import customFormats from './Settings/customFormats';
import customFormatSpecifications from './Settings/customFormatSpecifications';
import delayProfiles from './Settings/delayProfiles';
import downloadClientOptions from './Settings/downloadClientOptions';
import downloadClients from './Settings/downloadClients';

export * from './Settings/applications';
export * from './Settings/customFormatSpecifications.js';
export * from './Settings/customFormats';
export * from './Settings/delayProfiles';
export * from './Settings/downloadClients';
export * from './Settings/downloadClientOptions';

//
// Variables

export const section = 'settings';

//
// State

export const defaultState = {
  advancedSettings: false,
  applications: applications.defaultState,
  customFormatSpecifications: customFormatSpecifications.defaultState,
  customFormats: customFormats.defaultState,
  delayProfiles: delayProfiles.defaultState,
  downloadClients: downloadClients.defaultState,
  downloadClientOptions: downloadClientOptions.defaultState
};

export const persistState = [
  'settings.importListExclusions.pageSize'
];

//
// Action Handlers

export const actionHandlers = handleThunks({
  ...applications.actionHandlers,
  ...customFormatSpecifications.actionHandlers,
  ...customFormats.actionHandlers,
  ...delayProfiles.actionHandlers,
  ...downloadClients.actionHandlers,
  ...downloadClientOptions.actionHandlers
});

//
// Reducers

export const reducers = createHandleActions({
  ...applications.reducers,
  ...customFormatSpecifications.reducers,
  ...customFormats.reducers,
  ...delayProfiles.reducers,
  ...downloadClients.reducers,
  ...downloadClientOptions.reducers

}, defaultState, section);
