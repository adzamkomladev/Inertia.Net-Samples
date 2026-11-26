import { route } from '../../../../core';

export const useForm = route('get', '/features/forms/use-form');
export const submitUseForm = route('post', '/features/forms/use-form');
export const formComponent = route('get', '/features/forms/form-component');
export const submitFormComponent = route('post', '/features/forms/form-component');
export const fileUploads = route('get', '/features/forms/file-uploads');
export const submitFileUploads = route('post', '/features/forms/file-uploads');
export const validation = route('get', '/features/forms/validation');
export const submitValidation = route('post', '/features/forms/validation');
export const submitValidationSecondary = route(
    'post',
    '/features/forms/validation/secondary',
);
export const precognition = route('get', '/features/forms/precognition');
export const storeAccount = route('post', '/features/forms/precognition');
export const optimisticUpdates = route('get', '/features/forms/optimistic-updates');
export const toggleFavorite = route(
    'post',
    '/features/forms/optimistic-toggle/{contact}',
    [{ name: 'contact' }],
);
export const useFormContext = route('get', '/features/forms/use-form-context');
export const dottedKeys = route('get', '/features/forms/dotted-keys');
export const submitDottedKeys = route('post', '/features/forms/dotted-keys');
export const wayfinder = route('get', '/features/forms/wayfinder');
