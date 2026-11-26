import { route } from '../../../../core';

export const deferredProps = route('get', '/features/data-loading/deferred-props');
export const partialReloads = route('get', '/features/data-loading/partial-reloads');
export const infiniteScroll = route('get', '/features/data-loading/infinite-scroll');
export const whenVisible = route('get', '/features/data-loading/when-visible');
export const polling = route('get', '/features/data-loading/polling');
export const propMerging = route('get', '/features/data-loading/prop-merging');
export const onceProps = route('get', '/features/data-loading/once-props/{page?}', [
    { name: 'page', optional: true },
]);
export const optionalProps = route('get', '/features/data-loading/optional-props');
export const bigIntegers = route('get', '/features/data-loading/big-integers');
export const submitBigInteger = route('post', '/features/data-loading/big-integers');
export const liveProps = route('get', '/features/data-loading/live-props');
